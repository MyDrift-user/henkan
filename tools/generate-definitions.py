#!/usr/bin/env python3
"""Regenerates the Office and multi-step backend definitions.

The JSON files under src/Henkan.Core/Definitions are the source of truth and are
committed; this script exists because the interesting part of office.json is a
table of Office file format codes, and a table is far easier to check than two
hundred lines of generated JSON. Run it after changing a code or adding a route:

    python tools/generate-definitions.py

Nothing in the build depends on it.
"""

import io
import json
import os

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Henkan.Core", "Definitions")

WORD_IN = ["doc", "docx", "docm", "dot", "dotx", "dotm", "rtf", "txt",
           "htm", "html", "mht", "mhtml", "odt", "xml", "wps"]
EXCEL_IN = ["xls", "xlsx", "xlsm", "xlsb", "xlt", "xltx", "xltm", "csv",
            "txt", "prn", "dif", "slk", "ods", "xml", "htm", "html"]
PPT_IN = ["ppt", "pptx", "pptm", "pps", "ppsx", "ppsm", "pot", "potx", "odp"]

PDF_OPTIONS = {
    "word": [
        {"id": "OptimizeFor", "label": "Optimise for", "kind": "choice", "default": "Print",
         "choices": [{"value": "Print"}, {"value": "Screen", "label": "Screen (smaller)"}]},
        {"id": "CreateBookmarks", "label": "Bookmarks", "kind": "choice", "default": "None",
         "choices": [{"value": "None"}, {"value": "Headings", "label": "From headings"},
                     {"value": "Bookmarks", "label": "From Word bookmarks"}]},
        {"id": "IncludeDocumentProperties", "label": "Keep document properties", "kind": "boolean", "default": "true"},
        {"id": "PdfA", "label": "PDF/A compliant", "kind": "boolean", "default": "false"},
    ],
    "excel": [
        {"id": "Quality", "label": "Quality", "kind": "choice", "default": "Standard",
         "choices": [{"value": "Standard"}, {"value": "Minimum", "label": "Minimum (smaller)"}]},
        {"id": "IgnorePrintAreas", "label": "Ignore print areas", "kind": "boolean", "default": "false"},
        {"id": "IncludeDocumentProperties", "label": "Keep document properties", "kind": "boolean", "default": "true"},
    ],
}

SLIDE_IMAGE_OPTIONS = [
    {"id": "Slide", "label": "Slide", "kind": "integer", "default": "1", "minimum": 1, "maximum": 1000},
    {"id": "Width", "label": "Width", "kind": "choice", "default": "1920", "unit": "px",
     "choices": [{"value": "3840"}, {"value": "1920"}, {"value": "1280"}, {"value": "960"}, {"value": "640"}]},
]


def target(id, label, ext, operation, inputs, mode=None, fmt=None, category="Document",
           description=None, options=None, settings=None):
    settings = dict(settings or {})
    if mode:
        settings["mode"] = mode
    if fmt is not None:
        settings["format"] = str(fmt)

    entry = {"id": id, "label": label, "outputExtension": ext, "category": category}
    if description:
        entry["description"] = description
    entry["inputExtensions"] = inputs
    entry["operation"] = operation
    if settings:
        entry["settings"] = settings
    entry["options"] = options or []
    return entry


office = {
    "id": "office",
    "name": "Microsoft Office",
    "description": "Document conversion through COM automation of an installed Microsoft Office. Every format the installed applications can save is available.",
    "kind": "office",
    "timeoutSeconds": 300,
    "targets": [
        # Word. WdSaveFormat codes; PDF and XPS go through ExportAsFixedFormat.
        target("word-pdf", "PDF (Word)", "pdf", "word", WORD_IN,
               description="Laid out by Word itself, so it matches what printing would give you.",
               options=PDF_OPTIONS["word"]),
        target("word-xps", "XPS (Word)", "xps", "word", WORD_IN, options=PDF_OPTIONS["word"]),
        target("word-docx", "Word (DOCX)", "docx", "word", WORD_IN, mode="saveas", fmt=16),
        target("word-doc", "Word 97-2003 (DOC)", "doc", "word", WORD_IN, mode="saveas", fmt=0),
        target("word-rtf", "Rich text (RTF)", "rtf", "word", WORD_IN, mode="saveas", fmt=6,
               description="Formatted text that almost every word processor ever written can open."),
        target("word-odt", "OpenDocument text (ODT)", "odt", "word", WORD_IN, mode="saveas", fmt=23),
        target("word-txt", "Plain text", "txt", "word", WORD_IN, mode="saveas", fmt=2,
               description="Drops all formatting and keeps the words."),
        target("word-html", "HTML", "html", "word", WORD_IN, mode="saveas", fmt=10,
               description="Filtered HTML, without the Office markup that makes the file ten times larger."),

        # Excel. XlFileFormat codes.
        target("excel-pdf", "PDF (Excel)", "pdf", "excel", EXCEL_IN, options=PDF_OPTIONS["excel"]),
        target("excel-xps", "XPS (Excel)", "xps", "excel", EXCEL_IN, options=PDF_OPTIONS["excel"]),
        target("excel-xlsx", "Excel (XLSX)", "xlsx", "excel", EXCEL_IN, mode="saveas", fmt=51),
        target("excel-xls", "Excel 97-2003 (XLS)", "xls", "excel", EXCEL_IN, mode="saveas", fmt=56),
        target("excel-ods", "OpenDocument spreadsheet (ODS)", "ods", "excel", EXCEL_IN, mode="saveas", fmt=60),
        target("excel-csv", "CSV", "csv", "excel", EXCEL_IN, mode="saveas", fmt=62,
               description="UTF-8. A CSV holds one table, so the active sheet is what gets written.",
               settings={"activeSheetOnly": "true"}),
        target("excel-txt", "Tab separated text", "txt", "excel", EXCEL_IN, mode="saveas", fmt=42,
               settings={"activeSheetOnly": "true"}),
        target("excel-html", "HTML", "html", "excel", EXCEL_IN, mode="saveas", fmt=44),

        # PowerPoint. PpSaveAsFileType codes; images go one slide at a time.
        target("powerpoint-pdf", "PDF (PowerPoint)", "pdf", "powerpoint", PPT_IN, mode="saveas", fmt=32),
        target("powerpoint-xps", "XPS (PowerPoint)", "xps", "powerpoint", PPT_IN, mode="saveas", fmt=33),
        target("powerpoint-pptx", "PowerPoint (PPTX)", "pptx", "powerpoint", PPT_IN, mode="saveas", fmt=24),
        target("powerpoint-ppt", "PowerPoint 97-2003 (PPT)", "ppt", "powerpoint", PPT_IN, mode="saveas", fmt=1),
        target("powerpoint-odp", "OpenDocument presentation (ODP)", "odp", "powerpoint", PPT_IN, mode="saveas", fmt=35),
        target("powerpoint-png", "PNG (one slide)", "png", "powerpoint", PPT_IN, mode="slide-image",
               category="Image", description="Renders a single slide at whatever width you ask for.",
               settings={"imageFormat": "PNG"}, options=SLIDE_IMAGE_OPTIONS),
        target("powerpoint-jpg", "JPEG (one slide)", "jpg", "powerpoint", PPT_IN, mode="slide-image",
               category="Image", settings={"imageFormat": "JPG"}, options=SLIDE_IMAGE_OPTIONS),
    ],
}

RESOLUTION = {"id": "Resolution", "label": "Resolution", "kind": "choice", "default": "150", "unit": "dpi",
              "choices": [{"value": "72"}, {"value": "96"}, {"value": "150"}, {"value": "300"}, {"value": "600"}]}
PAGE = {"id": "Page", "label": "Page", "kind": "integer", "default": "1", "minimum": 1, "maximum": 10000,
        "description": "Which page of the document, once it has been laid out."}

TO_PDF = {"targets": ["office/word-pdf", "libreoffice/pdf"]}
SHEET_TO_PDF = {"targets": ["office/excel-pdf", "libreoffice/pdf"]}
SLIDES_TO_PDF = {"targets": ["office/powerpoint-pdf", "libreoffice/pdf"]}
ANY_TO_PDF = {"targets": ["libreoffice/pdf", "office/word-pdf"]}


def via(id, label, ext, inputs, first, second, category="Image", description=None, options=None):
    return {
        "id": id,
        "label": label,
        "outputExtension": ext,
        "category": category,
        "description": description,
        "inputExtensions": inputs,
        "steps": [first, second],
        "options": options or [],
    }


def raster(step_target, extra=None):
    options = {"Resolution": "{Resolution}", "Page": "{Page}"}
    options.update(extra or {})
    return {"targets": [step_target], "options": options}


pipeline = {
    "id": "pipeline",
    "name": "Multi-step",
    "description": "Conversions that pass through an intermediate format, so a document can reach an image and an image can reach a document.",
    "kind": "pipeline",
    "targets": [
        via("text-png", "PNG (via PDF)", "png", ["$text"], TO_PDF, raster("ghostscript/png"),
            description="Lays the document out first, so the picture matches the printed page.",
            options=[RESOLUTION, PAGE]),
        via("text-jpg", "JPEG (via PDF)", "jpg", ["$text"], TO_PDF, raster("ghostscript/jpg"),
            options=[RESOLUTION, PAGE]),
        via("text-tiff", "TIFF (via PDF)", "tiff", ["$text"], TO_PDF,
            {"targets": ["ghostscript/tiff"], "options": {"Resolution": "{Resolution}"}},
            description="Every page in one multi-page TIFF.", options=[RESOLUTION]),
        via("sheet-png", "PNG (via PDF)", "png", ["$spreadsheet"], SHEET_TO_PDF, raster("ghostscript/png"),
            description="A spreadsheet laid out as it would print, then rasterised.",
            options=[RESOLUTION, PAGE]),
        via("sheet-jpg", "JPEG (via PDF)", "jpg", ["$spreadsheet"], SHEET_TO_PDF, raster("ghostscript/jpg"),
            options=[RESOLUTION, PAGE]),
        via("sheet-tiff", "TIFF (via PDF)", "tiff", ["$spreadsheet"], SHEET_TO_PDF,
            {"targets": ["ghostscript/tiff"], "options": {"Resolution": "{Resolution}"}},
            options=[RESOLUTION]),
        via("slides-png", "PNG (via PDF)", "png", ["$presentation"], SLIDES_TO_PDF, raster("ghostscript/png"),
            options=[RESOLUTION, PAGE]),
        via("slides-jpg", "JPEG (via PDF)", "jpg", ["$presentation"], SLIDES_TO_PDF, raster("ghostscript/jpg"),
            options=[RESOLUTION, PAGE]),
        via("sheet-text", "Plain text (via PDF)", "txt", ["$spreadsheet"], SHEET_TO_PDF,
            {"targets": ["ghostscript/txt"]},
            category="Document",
            description="The words as they appear on the laid out page, not the raw cells.",
            options=[]),
        via("slides-text", "Plain text (via PDF)", "txt", ["$presentation"], SLIDES_TO_PDF,
            {"targets": ["ghostscript/txt"]},
            category="Document", options=[]),
        via("image-doc-pdf", "PDF (searchable layout)", "pdf", ["$image"],
            {"targets": ["imagemagick/pdf"]},
            {"targets": ["ghostscript/pdf"], "options": {"Profile": "{Profile}"}},
            category="Document",
            description="An image wrapped in a PDF and then recompressed, which is far smaller than the wrap alone.",
            options=[{"id": "Profile", "label": "Profile", "kind": "choice", "default": "ebook",
                      "choices": [{"value": "screen", "label": "Screen (72 dpi, smallest)"},
                                  {"value": "ebook", "label": "E-book (150 dpi)"},
                                  {"value": "printer", "label": "Printer (300 dpi)"},
                                  {"value": "prepress", "label": "Prepress"}]}]),
        via("pdf-webp", "WebP (via PNG)", "webp", ["$postscript"], raster("ghostscript/png"),
            {"targets": ["imagemagick/webp"], "options": {"Quality": "{Quality}"}},
            description="A page of a PDF as a WebP, which is a fraction of the size of the PNG.",
            options=[RESOLUTION, PAGE,
                     {"id": "Quality", "label": "Quality", "kind": "range", "default": "85",
                      "minimum": 1, "maximum": 100, "step": 1}]),
        via("pdf-avif", "AVIF (via PNG)", "avif", ["$postscript"], raster("ghostscript/png"),
            {"targets": ["imagemagick/avif"], "options": {"Quality": "{Quality}"}},
            options=[RESOLUTION, PAGE,
                     {"id": "Quality", "label": "Quality", "kind": "range", "default": "70",
                      "minimum": 1, "maximum": 100, "step": 1}]),
        via("pdf-ico", "Icon (via PNG)", "ico", ["$postscript"], raster("ghostscript/png"),
            {"targets": ["imagemagick/ico"], "options": {"IconSizes": "{IconSizes}"}},
            options=[RESOLUTION, PAGE,
                     {"id": "IconSizes", "label": "Sizes", "kind": "text", "default": "16, 32, 48, 256"}]),
    ],
}


# Single-step routes with alternatives. A step naming several targets takes the
# first available one, so with one step this is a choice rather than a chain:
# "make a PDF with Microsoft Office, or with LibreOffice, whichever is here".
# One preset and one menu entry instead of one per office suite.
def route(id, label, ext, inputs, targets, category="Document", description=None, options=None):
    return {
        "id": id,
        "label": label,
        "outputExtension": ext,
        "category": category,
        "description": description,
        "inputExtensions": inputs,
        "steps": [{"targets": targets}],
        "options": options or [],
    }


TEXT = ["$text"]
SHEET = ["$spreadsheet"]
SLIDES = ["$presentation"]

routes = [
    route("text-pdf", "PDF", "pdf", TEXT, ["office/word-pdf", "libreoffice/pdf"],
          description="Through Microsoft Word, or LibreOffice on a machine without it."),
    route("sheet-pdf", "PDF", "pdf", SHEET, ["office/excel-pdf", "libreoffice/pdf"],
          description="Through Microsoft Excel, or LibreOffice on a machine without it."),
    route("slides-pdf", "PDF", "pdf", SLIDES, ["office/powerpoint-pdf", "libreoffice/pdf"],
          description="Through Microsoft PowerPoint, or LibreOffice on a machine without it."),

    route("text-docx", "Word (DOCX)", "docx", TEXT, ["office/word-docx", "libreoffice/docx"]),
    route("text-odt", "OpenDocument text (ODT)", "odt", TEXT, ["office/word-odt", "libreoffice/odt"]),
    route("text-rtf", "Rich text (RTF)", "rtf", TEXT, ["office/word-rtf", "libreoffice/rtf"]),
    route("text-html", "HTML", "html", TEXT, ["office/word-html", "libreoffice/html"]),
    route("text-txt", "Plain text", "txt", TEXT, ["office/word-txt", "libreoffice/txt"],
          description="Drops all formatting and keeps the words."),

    route("sheet-xlsx", "Excel (XLSX)", "xlsx", SHEET, ["office/excel-xlsx", "libreoffice/xlsx"]),
    route("sheet-ods", "OpenDocument spreadsheet (ODS)", "ods", SHEET, ["office/excel-ods", "libreoffice/ods"]),
    route("sheet-csv", "CSV", "csv", SHEET, ["office/excel-csv", "libreoffice/csv"],
          description="One table, so the first or active sheet is what gets written."),
    route("sheet-html", "HTML", "html", SHEET, ["office/excel-html", "libreoffice/html"]),

    route("slides-pptx", "PowerPoint (PPTX)", "pptx", SLIDES, ["office/powerpoint-pptx", "libreoffice/pptx"]),
    route("slides-odp", "OpenDocument presentation (ODP)", "odp", SLIDES, ["office/powerpoint-odp", "libreoffice/odp"]),
]

existing = {t["id"] for t in pipeline["targets"]}
pipeline["targets"] = [t for t in routes if t["id"] not in existing] + pipeline["targets"]

for name, document in (("office.json", office), ("pipeline.json", pipeline)):
    path = os.path.join(ROOT, name)
    text = json.dumps(document, indent=2, ensure_ascii=False)
    io.open(path, "w", encoding="utf-8", newline="").write(text + "\n")
    print(name, len(document["targets"]), "targets")
