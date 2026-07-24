/**
 * XlsxExportHelper — turns an HTML <table> string into a real .xlsx file
 * (via SheetJS) instead of the old "HTML saved as .xls" trick, which some
 * Excel builds refuse to open because the extension doesn't match the
 * actual file content.
 *
 * Cells that must stay text (e.g. employee codes with leading zeros) should
 * use `data-t="s"` instead of the old `style="mso-number-format:'@@'"` hint.
 */
(function (window) {
    'use strict';

    window.exportHtmlTableToXlsx = function (html, fileName) {
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var table = doc.querySelector('table');
        var wb = XLSX.utils.table_to_book(table);
        XLSX.writeFile(wb, fileName);
    };
})(window);
