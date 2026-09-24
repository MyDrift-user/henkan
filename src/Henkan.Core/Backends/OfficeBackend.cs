using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Henkan.Core.Backends;

/// <summary>
/// Exports Office documents through COM automation of an installed Microsoft
/// Office.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the NetOffice dependency that a .NET Framework application would
/// reach for. NetOffice never shipped anything newer than net45, so it cannot be
/// referenced from a modern .NET project at all, and it was itself only a
/// late-binding wrapper over the same COM objects called here. Going direct costs
/// nothing and removes a dead dependency.
/// </para>
/// <para>
/// If Office is not installed the backend reports unavailable and its targets
/// disappear from the picker. The bundled LibreOffice definition covers the same
/// formats as a process backend for machines without Office.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class OfficeBackend : IConversionBackend
{
    private const int WordExportFormatPdf = 17;
    private const int WordExportFormatXps = 18;
    private const int ExcelTypePdf = 0;
    private const int ExcelTypeXps = 1;

    public OfficeBackend(BackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        this.Definition = definition;
    }

    public BackendDefinition Definition { get; }

    public BackendAvailability Availability { get; private set; } = BackendAvailability.Unknown;

    public Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        // Registration is enough to answer the question. Actually starting Word
        // just to probe would cost seconds on every launch.
        List<string> found = [];

        foreach (string progId in (string[])["Word.Application", "Excel.Application", "PowerPoint.Application"])
        {
            if (Type.GetTypeFromProgID(progId) is not null)
            {
                found.Add(progId.Split('.')[0]);
            }
        }

        this.Availability = found.Count > 0
            ? BackendAvailability.Available(null, string.Join(", ", found))
            : BackendAvailability.Unavailable(
                "Microsoft Office is not installed. Document conversions use LibreOffice instead when it is installed.");

        return Task.FromResult(this.Availability);
    }

    public async Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!this.Availability.IsAvailable)
        {
            await this.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!this.Availability.IsAvailable)
        {
            throw new ConversionFailedException(
                this.Availability.Reason ?? "Microsoft Office is not available.");
        }

        string operation = context.Target.Operation
            ?? throw new ConversionFailedException(
                $"Target \"{context.Target.Id}\" does not say which Office application to use.");

        string mode = context.Target.Setting("mode") ?? "fixed";

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPath)!);
        context.Write($"Automating {operation} to produce \"{Path.GetFileName(context.OutputPath)}\".");
        context.Report(0.1d);

        // A dialog Office decides to show anyway blocks the automation call for
        // good, and the hidden instance behind it would outlive the job. When the
        // job is cancelled or times out, the instance this job started is ended;
        // one it merely attached to, as PowerPoint does, is left alone.
        var owned = new OwnedProcesses();
        using CancellationTokenRegistration ending = cancellationToken.Register(owned.EndAll);

        // Office automation expects a single-threaded apartment. The thread pool
        // is multi-threaded, so the call gets its own STA thread.
        await RunOnStaThreadAsync(
            () =>
            {
                switch (operation.ToLowerInvariant())
                {
                    case "word":
                        ConvertWord(context, mode, owned);
                        break;

                    case "excel":
                        ConvertExcel(context, mode, owned);
                        break;

                    case "powerpoint":
                        ConvertPowerPoint(context, mode, owned, cancellationToken);
                        break;

                    default:
                        throw new ConversionFailedException($"Unknown Office operation \"{operation}\".");
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(context.OutputPath))
        {
            throw new ConversionFailedException(
                $"Office reported success but produced no file at \"{context.OutputPath}\".");
        }

        context.Report(1d);
    }

    private static void ConvertWord(ConversionContext context, string mode, OwnedProcesses owned)
    {
        dynamic? application = null;
        dynamic? documents = null;
        dynamic? document = null;

        try
        {
            application = CreateApplication("Word.Application", owned);
            application.Visible = false;
            application.DisplayAlerts = 0;

            documents = application.Documents;

            // FileName, ConfirmConversions, ReadOnly, AddToRecentFiles
            document = documents.Open(context.InputPath, false, true, false);

            if (mode.Equals("saveas", StringComparison.OrdinalIgnoreCase))
            {
                UsePrintLayout(document);
                document.SaveAs2(context.OutputPath, RequireFormat(context));
            }
            else
            {
                int optimizeFor = context.Options.GetString("OptimizeFor")?.Equals("Screen", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
                bool includeProperties = context.Options.GetBoolean("IncludeDocumentProperties", true);
                int bookmarks = context.Options.GetString("CreateBookmarks")?.ToLowerInvariant() switch
                {
                    "headings" => 1,
                    "bookmarks" => 2,
                    _ => 0,
                };
                bool pdfA = context.Options.GetBoolean("PdfA");
                int format = context.Target.OutputExtension.Equals("xps", StringComparison.OrdinalIgnoreCase)
                    ? WordExportFormatXps
                    : WordExportFormatPdf;

                // OutputFileName, ExportFormat, OpenAfterExport, OptimizeFor, Range,
                // From, To, Item, IncludeDocProps, KeepIRM, CreateBookmarks,
                // DocStructureTags, BitmapMissingFonts, UseISO19005_1
                document.ExportAsFixedFormat(
                    context.OutputPath,
                    format,
                    false,
                    optimizeFor,
                    0,
                    1,
                    1,
                    0,
                    includeProperties,
                    true,
                    bookmarks,
                    true,
                    true,
                    pdfA);
            }

            document.Close(0);
            document = null;
        }
        finally
        {
            QuitAndRelease(application, documents, document);
        }
    }

    /// <summary>
    /// Switches the document to Print Layout before it is saved.
    /// </summary>
    /// <remarks>
    /// A hidden Word opens documents in Web Layout, and the view is saved with
    /// the file: the result then opens full width without pages, although the
    /// page itself is A4 or Letter as it should be. Print Layout is what anyone
    /// opening a Word document expects.
    /// </remarks>
    private static void UsePrintLayout(dynamic document)
    {
        const int WdPrintView = 3;

        try
        {
            document.Windows[1].View.Type = WdPrintView;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Without a window there is no view to change, and the file is
            // still correct; only the view it opens in is affected.
        }
    }

    private static void ConvertExcel(ConversionContext context, string mode, OwnedProcesses owned)
    {
        dynamic? application = null;
        dynamic? workbooks = null;
        dynamic? workbook = null;

        try
        {
            application = CreateApplication("Excel.Application", owned);
            application.Visible = false;
            application.DisplayAlerts = false;

            workbooks = application.Workbooks;
            workbook = workbooks.Open(context.InputPath, 0, true);

            if (mode.Equals("saveas", StringComparison.OrdinalIgnoreCase))
            {
                // A workbook with several sheets cannot become one CSV, so the
                // active sheet is what gets written. Saying so beats handing back
                // a file the user has to discover is incomplete.
                if (context.Target.Setting("activeSheetOnly") == "true" && (int)workbook.Worksheets.Count > 1)
                {
                    context.Write($"Only the active sheet is written; the workbook has {workbook.Worksheets.Count}.");
                }

                workbook.SaveAs(context.OutputPath, RequireFormat(context));
            }
            else
            {
                int quality = context.Options.GetString("Quality")?.Equals("Minimum", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0;
                bool includeProperties = context.Options.GetBoolean("IncludeDocumentProperties", true);
                bool ignorePrintAreas = context.Options.GetBoolean("IgnorePrintAreas");
                int type = context.Target.OutputExtension.Equals("xps", StringComparison.OrdinalIgnoreCase)
                    ? ExcelTypeXps
                    : ExcelTypePdf;

                // Type, Filename, Quality, IncludeDocProperties, IgnorePrintAreas
                workbook.ExportAsFixedFormat(
                    type,
                    context.OutputPath,
                    quality,
                    includeProperties,
                    ignorePrintAreas);
            }

            workbook.Close(false);
            workbook = null;
        }
        finally
        {
            QuitAndRelease(application, workbooks, workbook);
        }
    }

    private static void ConvertPowerPoint(ConversionContext context, string mode, OwnedProcesses owned, CancellationToken cancellationToken)
    {
        dynamic? application = null;
        dynamic? presentations = null;
        dynamic? presentation = null;

        try
        {
            application = CreateApplication("PowerPoint.Application", owned);

            presentations = application.Presentations;

            // FileName, ReadOnly (msoTrue), Untitled (msoFalse), WithWindow (msoFalse).
            // PowerPoint refuses to set Visible = false, so the window is
            // suppressed at open time instead.
            presentation = presentations.Open(context.InputPath, -1, 0, 0);

            if (mode.Equals("slide-image", StringComparison.OrdinalIgnoreCase))
            {
                ExportSlide(context, presentation);
            }
            else if (mode.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                ExportVideo(context, presentation, cancellationToken);
            }
            else
            {
                // SaveAs rather than ExportAsFixedFormat: the latter takes a long
                // tail of optional arguments that the late-bound call cannot
                // marshal, and it fails on the file name before it starts.
                presentation.SaveAs(context.OutputPath, RequireFormat(context));
            }

            presentation.Close();
            presentation = null;
        }
        finally
        {
            QuitAndRelease(application, presentations, presentation);
        }
    }

    /// <summary>
    /// Writes one slide as an image. SaveAs to an image format produces a folder
    /// of files for a deck with more than one slide, which is not a result anyone
    /// can predict from a file name, so a single slide is exported by hand.
    /// </summary>
    private static void ExportSlide(ConversionContext context, dynamic presentation)
    {
        dynamic slides = presentation.Slides;
        int count = (int)slides.Count;
        int wanted = Math.Clamp(context.Options.GetInt32("Slide", 1), 1, Math.Max(count, 1));

        if (count > 1)
        {
            context.Write($"Exporting slide {wanted} of {count}.");
        }

        int width = context.Options.GetInt32("Width", 1920);
        string format = context.Target.Setting("imageFormat") ?? "PNG";

        dynamic slide = slides[wanted];
        double slideWidth = (double)presentation.PageSetup.SlideWidth;
        double slideHeight = (double)presentation.PageSetup.SlideHeight;
        int height = (int)Math.Round(width * (slideHeight / slideWidth));

        slide.Export(context.OutputPath, format, width, height);
    }

    /// <summary>
    /// Records the deck as a video, MP4 or WMV by the output's extension.
    /// </summary>
    /// <remarks>
    /// PowerPoint renders in the background and returns at once, so closing the
    /// deck straight after the call would abandon a half written file. The status
    /// is polled until PowerPoint says it is done or failed.
    /// </remarks>
    private static void ExportVideo(ConversionContext context, dynamic presentation, CancellationToken cancellationToken)
    {
        int height = context.Options.GetInt32("Height", 1080);
        int seconds = Math.Max(1, context.Options.GetInt32("SecondsPerSlide", 5));
        int framesPerSecond = context.Options.GetInt32("FramesPerSecond", 30);
        bool useTimings = context.Options.GetBoolean("UseTimings", true);
        int slides = (int)presentation.Slides.Count;

        context.Write($"Recording {slides} slide{(slides == 1 ? string.Empty : "s")} at {height}p.");

        // FileName, UseTimingsAndNarrations, DefaultSlideDuration,
        // VertResolution, FramesPerSecond, Quality
        presentation.CreateVideo(context.OutputPath, useTimings, seconds, height, framesPerSecond, 85);

        DateTime started = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // PpMediaTaskStatus: 1 in progress, 2 queued, 3 done, 4 failed.
            int status = (int)presentation.CreateVideoStatus;

            if (status == 3)
            {
                break;
            }

            if (status == 4)
            {
                throw new ConversionFailedException("PowerPoint could not record the video.");
            }

            // No percentage is on offer, so the bar creeps towards what a deck of
            // this length usually takes rather than sitting still.
            double expected = Math.Max(10d, slides * seconds * 0.6d);
            context.Report(0.1d + (0.85d * Math.Min(1d, (DateTime.UtcNow - started).TotalSeconds / expected)));

            Thread.Sleep(500);
        }
    }

    private static int RequireFormat(ConversionContext context)
    {
        if (!int.TryParse(context.Target.Setting("format"), out int format))
        {
            throw new ConversionFailedException(
                $"Target \"{context.Target.Id}\" is missing a numeric \"format\" setting.");
        }

        return format;
    }

    private static dynamic CreateApplication(string progId, OwnedProcesses owned)
    {
        string product = "Microsoft " + progId.Split('.')[0];

        Type type = Type.GetTypeFromProgID(progId)
            ?? throw new ConversionFailedException($"{product} is not installed.");

        string image = progId.Split('.')[0] switch
        {
            "Word" => "WINWORD",
            "Excel" => "EXCEL",
            _ => "POWERPNT",
        };

        HashSet<int> before = Running(image);

        object application = Activator.CreateInstance(type)
            ?? throw new ConversionFailedException($"{product} could not be started.");

        owned.Add(Running(image).Except(before));
        return application;
    }

    private static HashSet<int> Running(string image)
    {
        Process[] processes = Process.GetProcessesByName(image);

        try
        {
            return [.. processes.Select(p => p.Id)];
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>The Office processes one job started, so a stuck one can be ended.</summary>
    private sealed class OwnedProcesses
    {
        private readonly List<int> ids = [];

        public void Add(IEnumerable<int> started)
        {
            lock (this.ids)
            {
                this.ids.AddRange(started);
            }
        }

        public void EndAll()
        {
            int[] snapshot;

            lock (this.ids)
            {
                snapshot = [.. this.ids];
            }

            foreach (int id in snapshot)
            {
                try
                {
                    using Process process = Process.GetProcessById(id);
                    process.Kill();
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone, which is what was wanted.
                }
            }
        }
    }


    /// <summary>
    /// Releases the COM objects and shuts the host application down. Skipping
    /// this leaves an invisible WINWORD.EXE running for every converted file.
    /// </summary>
    private static void QuitAndRelease(dynamic? application, dynamic? collection, dynamic? document)
    {
        Release(document);
        Release(collection);

        if (application is not null)
        {
            try
            {
                application.Quit();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                // The application may already be gone; there is nothing useful to do.
            }

            Release(application);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.FinalReleaseComObject(comObject);
            }
            catch (ArgumentException)
            {
                // Not a live RCW any more.
            }
        }
    }

    private static Task RunOnStaThreadAsync(Action action, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                completion.TrySetResult();
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Henkan Office automation",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }
}
