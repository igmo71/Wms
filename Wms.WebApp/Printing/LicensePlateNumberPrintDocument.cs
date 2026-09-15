using System.Net;
using System.Text;
using Wms.Domain;
using ZXing;
using ZXing.Common;

namespace Wms.WebApp.Printing;

public static class LicensePlateNumberPrintDocument
{
    public static string Render(IReadOnlyList<LicensePlateNumber> labels)
    {
        var html = new StringBuilder("""
            <!doctype html><html lang="ru"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Этикетки LPN — A4</title>
            <style>
            *{box-sizing:border-box}body{margin:0;background:#eee;color:#000;font-family:Arial,sans-serif}
            header{padding:16px;text-align:center}button{padding:10px 24px;font-size:16px;cursor:pointer}
            .sheet{width:210mm;height:297mm;padding:10mm;margin:12px auto;background:white;
                display:grid;grid-template-columns:repeat(2,95mm);grid-template-rows:repeat(5,55mm);align-content:start}
            .label{padding:5mm;border:0.2mm dashed #bbb;text-align:center;break-inside:avoid;overflow:hidden}
            .barcode svg{display:block;width:84mm;height:25mm;margin:auto}
            .code{font:700 17pt monospace;margin-top:2mm}.caption{font-size:9pt;margin-bottom:2mm}
            @page{size:A4 portrait;margin:0}
            @media print{body{background:white}header{display:none}.sheet{margin:0;break-after:page}.sheet:last-child{break-after:auto}}
            </style></head><body><header><h1>Этикетки LPN</h1>
            <p>А4, книжная ориентация, масштаб 100%, без полей и колонтитулов. 10 этикеток на листе.</p>
            <p>Повторная печать использует те же коды. Не наклеивайте копии одного кода на разные палеты.</p>
            <button onclick="window.print()">Печать</button></header>
            """);
        var writer = new BarcodeWriterSvg
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Width = 840, Height = 250, Margin = 20, PureBarcode = true }
        };
        foreach (var page in labels.Chunk(10))
        {
            html.Append("<section class=\"sheet\">");
            foreach (var label in page)
            {
                var svg = writer.Write(label.Code).Content;
                // Embed only the SVG element, without an XML declaration or document type inside HTML.
                svg = svg[svg.IndexOf("<svg", StringComparison.Ordinal)..];
                html.Append("<article class=\"label\"><div class=\"caption\">WMS · LPN</div><div class=\"barcode\">")
                    .Append(svg)
                    .Append("</div><div class=\"code\">").Append(WebUtility.HtmlEncode(label.Code))
                    .Append("</div></article>");
            }
            html.Append("</section>");
        }
        return html.Append("</body></html>").ToString();
    }
}
