using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace XmlA1 {
 public static class QueueSpreadsheet {
  public static void Save(string path,IEnumerable<KeyJob> jobs) {
   XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main",r="http://schemas.openxmlformats.org/officeDocument/2006/relationships",p="http://schemas.openxmlformats.org/package/2006/relationships",ct="http://schemas.openxmlformats.org/package/2006/content-types";
   var rows=new List<string[]>{new[]{"Chave","Situação","Código SEFAZ","Tentativas","Detalhes"}};rows.AddRange(jobs.Select(j=>new[]{j.Key,j.Status,j.Code,j.Attempts.ToString(),j.Message}));
   var sheetData=new XElement(s+"sheetData");for(int i=0;i<rows.Count;i++){var row=new XElement(s+"row",new XAttribute("r",i+1));for(int col=0;col<5;col++)row.Add(new XElement(s+"c",new XAttribute("r",((char)('A'+col)).ToString()+(i+1)),new XAttribute("t","inlineStr"),new XElement(s+"is",new XElement(s+"t",new XAttribute(XNamespace.Xml+"space","preserve"),rows[i][col]??""))));sheetData.Add(row);}
   var sheet=new XDocument(new XElement(s+"worksheet",new XElement(s+"cols",new XElement(s+"col",new XAttribute("min",1),new XAttribute("max",1),new XAttribute("width",48),new XAttribute("customWidth",1)),new XElement(s+"col",new XAttribute("min",2),new XAttribute("max",4),new XAttribute("width",20),new XAttribute("customWidth",1)),new XElement(s+"col",new XAttribute("min",5),new XAttribute("max",5),new XAttribute("width",80),new XAttribute("customWidth",1))),sheetData,new XElement(s+"autoFilter",new XAttribute("ref","A1:E"+rows.Count))));
   var types=new XDocument(new XElement(ct+"Types",new XElement(ct+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(ct+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
   using(var memory=new MemoryStream()) {
    using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) {
     Add(zip,"[Content_Types].xml",types);
     Add(zip,"_rels/.rels",new XDocument(new XElement(p+"Relationships",new XElement(p+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",r.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml")))));
     Add(zip,"xl/workbook.xml",new XDocument(new XElement(s+"workbook",new XAttribute(XNamespace.Xmlns+"r",r),new XElement(s+"sheets",new XElement(s+"sheet",new XAttribute("name","Resultados"),new XAttribute("sheetId",1),new XAttribute(r+"id","rId1"))))));
     Add(zip,"xl/_rels/workbook.xml.rels",new XDocument(new XElement(p+"Relationships",new XElement(p+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",r.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/sheet1.xml")))));
     Add(zip,"xl/worksheets/sheet1.xml",sheet);
    }Rules.WriteAtomic(path,memory.ToArray());
   }
  }
  static void Add(ZipArchive zip,string name,XDocument xml){var e=zip.CreateEntry(name,CompressionLevel.Optimal);byte[] data=Rules.Utf8(xml);using(var stream=e.Open())stream.Write(data,0,data.Length);}
 }
}
