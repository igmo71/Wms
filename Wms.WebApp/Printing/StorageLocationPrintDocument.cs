using System.Net;
using System.Text;
using Wms.Domain;
using ZXing;
using ZXing.Common;

namespace Wms.WebApp.Printing;

public static class StorageLocationPrintDocument
{
    public static string Render(IReadOnlyList<string> codes)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="ru"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Этикетки ячеек — A4</title>
            <style>
            *{box-sizing:border-box}body{margin:0;background:#eee;color:#000;font-family:Arial,sans-serif}
            header{padding:16px;text-align:center}button{padding:10px 24px;font-size:16px;cursor:pointer}
            .sheet{width:210mm;height:297mm;padding:10mm;margin:12px auto;background:#fff;
                display:grid;grid-template-columns:repeat(2,95mm);grid-template-rows:repeat(6,40mm);align-content:start}
            .label{height:40mm;padding:2mm 4mm;border:0.2mm dashed #bbb;text-align:center;
                break-inside:avoid;overflow:hidden;background:#fff}
            .address{font:700 25pt Arial,sans-serif;white-space:nowrap;line-height:1.1;margin:1mm 0 2mm}
            .barcode svg{display:block;width:84mm;height:21mm;margin:auto}
            @page{size:A4 portrait;margin:0}
            @media print{body{background:#fff}header{display:none}.sheet{margin:0;break-after:page}.sheet:last-child{break-after:auto}}
            </style></head><body><header><h1>Этикетки ячеек</h1>
            <p>A4, книжная ориентация, масштаб 100%, без полей и колонтитулов. 12 этикеток на листе.</p>
            <button onclick="window.print()">Печать</button></header>
            """);
        var writer = new BarcodeWriterSvg
        {
            Format = BarcodeFormat.CODE_39,
            Options = new EncodingOptions { Width = 840, Height = 210, Margin = 10, PureBarcode = true }
        };
        foreach (var page in codes.Chunk(12))
        {
            html.Append("<section class=\"sheet\">");
            foreach (var code in page)
            {
                var svg = writer.Write(code).Content;
                svg = svg[svg.IndexOf("<svg", StringComparison.Ordinal)..];
                html.Append("<article class=\"label\"><div class=\"address\">")
                    .Append(WebUtility.HtmlEncode(StorageLocationAddress.Format(code).Replace('-', '·')))
                    .Append("</div><div class=\"barcode\">")
                    .Append(svg)
                    .Append("</div></article>");
            }
            html.Append("</section>");
        }
        return html.Append("</body></html>").ToString();
    }
}
