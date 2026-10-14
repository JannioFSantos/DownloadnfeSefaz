using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace XmlA1 {
 /// <summary>Valida identificadores e centraliza leitura XML segura e gravação atômica.</summary>
 public static class Rules {
  public const string NfeNs = "http://www.portalfiscal.inf.br/nfe";
  public static string Cnpj(string value) {
   string s=Regex.Replace((value??"").ToUpperInvariant(),@"[.\-/\s]","");
   if(!Regex.IsMatch(s,@"^[A-Z0-9]{12}[0-9]{2}$") || s.Distinct().Count()==1) throw new InvalidOperationException("Informe um CNPJ válido, com 14 posições.");
   int[] a={5,4,3,2,9,8,7,6,5,4,3,2},b={6,5,4,3,2,9,8,7,6,5,4,3,2};
   int v=0;for(int i=0;i<12;i++)v+=(s[i]-48)*a[i];int d=v%11;d=d<2?0:11-d;
   if(s[12]-48!=d)throw new InvalidOperationException("Dígito verificador do CNPJ inválido.");
   v=0;for(int i=0;i<13;i++)v+=(s[i]-48)*b[i];d=v%11;d=d<2?0:11-d;
   if(s[13]-48!=d)throw new InvalidOperationException("Dígito verificador do CNPJ inválido.");return s;
  }
  public static string Nsu(string value) {
   if(!Regex.IsMatch(value??"",@"^\d{1,15}$"))throw new InvalidDataException("NSU inválido.");return value.PadLeft(15,'0');
  }
  public static bool Key(string value) {
   if(!Regex.IsMatch(value??"",@"^[0-9]{6}[A-Z0-9]{12}[0-9]{26}$") || value.Substring(20,2)!="55")return false;
   int n=0,w=2;for(int i=42;i>=0;i--){n+=(value[i]-48)*w;w=w==9?2:w+1;}int dv=11-n%11;if(dv>=10)dv=0;return dv==value[43]-48;
  }
  public static XDocument Xml(byte[] bytes) {
   if(bytes.Length>20*1024*1024)throw new InvalidDataException("XML acima do limite de 20 MB.");
   var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024,MaxCharactersFromEntities=0};
   using(var input=new MemoryStream(bytes))using(var reader=XmlReader.Create(input,settings))return XDocument.Load(reader,LoadOptions.PreserveWhitespace);
  }
  public static string Child(XElement node,string name){var e=node==null?null:node.Element(XName.Get(name,NfeNs));return e==null?"":e.Value;}
  public static byte[] Utf8(XDocument xml){return new UTF8Encoding(false).GetBytes(xml.ToString(SaveOptions.DisableFormatting));}
  public static void WriteAtomic(string path,byte[] bytes) {
   Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try {using(var f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes,0,bytes.Length);f.Flush(true);}if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
  }
  public static string Hash(byte[] bytes){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 }
 /// <summary>Sessão local do A1. Dispose libera o certificado e seu contêiner temporário.</summary>
 public sealed class CertificateSession:IDisposable {
  public X509Certificate2 Certificate{get;private set;}
  public string Cnpj{get;private set;}
  public CertificateSession(string path,string password) {
   X509Certificate2 cert=null;
   try {
    // UserKeySet without PersistKeySet: Schannel can use the temporary Windows key container.
    cert=new X509Certificate2(path,password,X509KeyStorageFlags.UserKeySet);
    if(!cert.HasPrivateKey)throw new InvalidOperationException("O arquivo não contém a chave privada do certificado A1.");
    if(DateTime.Now<cert.NotBefore || DateTime.Now>cert.NotAfter)throw new InvalidOperationException("O certificado está vencido ou ainda não está válido.");
    Cnpj=FindCnpj(cert);if(Cnpj=="")throw new InvalidOperationException("Não foi encontrado o CNPJ na extensão ICP-Brasil do certificado. Utilize um e-CNPJ A1.");
    using(var rsa=cert.GetRSAPrivateKey()){if(rsa==null)throw new InvalidOperationException("É necessário um certificado A1 com chave RSA.");rsa.SignData(new byte[]{1,2,3},HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);}
    Certificate=cert;cert=null;
   } catch(CryptographicException){throw new InvalidOperationException("Não foi possível abrir o A1. Confira a senha e o arquivo PFX/P12.");}finally{if(cert!=null)cert.Dispose();}
  }
  public void Validate(string cnpj) {
   if(Certificate==null || DateTime.Now>Certificate.NotAfter || DateTime.Now<Certificate.NotBefore)throw new InvalidOperationException("Carregue um certificado A1 válido.");
   cnpj=Rules.Cnpj(cnpj);if(cnpj.Substring(0,8)!=Cnpj.Substring(0,8))throw new InvalidOperationException("O CNPJ consultado precisa ter a mesma raiz do certificado.");
  }
  public static string FindCnpj(X509Certificate2 cert) {
   foreach(var ext in cert.Extensions)if(ext.Oid.Value=="2.5.29.17") {
    byte[] d=ext.RawData;foreach(var node in Der.Read(d,0,d.Length))foreach(var other in node.Children) {
     if(other.Tag!=0xa0 || other.Children.Count<2)continue;var oid=other.Children[0];
     if(oid.Tag!=6 || !oid.Bytes.SequenceEqual(new byte[]{0x60,0x4c,0x01,0x03,0x03}))continue;
     foreach(var candidate in other.Children[1].Leaves()) {
      string value=Encoding.UTF8.GetString(candidate.Bytes).Trim('\0',' ','\r','\n');
      try{return Rules.Cnpj(value);}catch(InvalidOperationException){}
     }
    }
   }return "";
  }
  public void Dispose(){if(Certificate!=null){Certificate.Dispose();Certificate=null;}}
 }
 internal sealed class Der {
  public byte Tag;public byte[] Bytes;public List<Der> Children=new List<Der>();
  public IEnumerable<Der> Leaves(){if(Children.Count==0)yield return this;else foreach(var c in Children)foreach(var n in c.Leaves())yield return n;}
  public static List<Der> Read(byte[] d,int start,int end,int depth=0) {
   if(depth>12)throw new InvalidDataException("Extensão do certificado inválida.");var nodes=new List<Der>();int p=start;
   while(p<end){if(p+2>end)throw new InvalidDataException("ASN.1 truncado.");byte tag=d[p++];int len=d[p++];if(len>=128){int count=len&127;len=0;if(count==0 || count>4 || p+count>end)throw new InvalidDataException("ASN.1 inválido.");for(int i=0;i<count;i++)len=checked(len*256+d[p++]);}if(len<0||len>end-p)throw new InvalidDataException("ASN.1 truncado.");var node=new Der{Tag=tag,Bytes=d.Skip(p).Take(len).ToArray()};if((tag&32)!=0)node.Children=Read(d,p,p+len,depth+1);nodes.Add(node);p+=len;}return nodes;
  }
 }
 public sealed class DocItem {
  public string Category{get;set;}public string Key{get;set;}public string Number{get;set;}public string Date{get;set;}public string Partner{get;set;}public string Cnpj{get;set;}public decimal Amount{get;set;}public string Status{get;set;}public string FilePath{get;set;}public string Nsu{get;set;}public string Environment{get;set;}public string NoteSituation{get;set;}public string EventType{get;set;}
 }
 /// <summary>Preserva os bytes recebidos e separa NF-e completa, resumos e eventos.</summary>
 public sealed class XmlLibrary {
  public string Root;public string Cnpj;public int Environment;
  public string Folder{get{return Path.Combine(Root,Cnpj,Environment==1?"Producao":"Homologacao");}}
  public XmlLibrary(string root,string cnpj,int env){Root=Path.GetFullPath(root);Cnpj=Rules.Cnpj(cnpj);Environment=env;}
  public DocItem Describe(byte[] data,string path="") {
   var doc=Rules.Xml(data);var root=doc.Root;XNamespace n=Rules.NfeNs;
   if(root==null||root.Name.NamespaceName!=Rules.NfeNs)throw new InvalidDataException("XML não pertence ao leiaute nacional de NF-e.");
   var item=new DocItem{FilePath=path,Key="",Number="",Date="",Partner="",Cnpj="",Status="",Category="Outros",Environment=""};
   if(root.Name.LocalName=="nfeProc") {
    var info=root.Element(n+"NFe")==null?null:root.Element(n+"NFe").Element(n+"infNFe");var prot=root.Element(n+"protNFe")==null?null:root.Element(n+"protNFe").Element(n+"infProt");
    if(info==null||prot==null)throw new InvalidDataException("NF-e sem conteúdo ou protocolo de autorização.");
    item.Key=((string)info.Attribute("Id")??"").Replace("NFe","");if(!Rules.Key(item.Key)||Rules.Child(prot,"chNFe")!=item.Key)throw new InvalidDataException("Chave ou protocolo da NF-e inválido.");
    var ide=info.Element(n+"ide");item.Environment=Rules.Child(ide,"tpAmb");if(item.Environment!=Environment.ToString())throw new InvalidDataException("O XML pertence a outro ambiente.");
    string emit=Rules.Child(info.Element(n+"emit"),"CNPJ"),dest=Rules.Child(info.Element(n+"dest"),"CNPJ");
    if(emit==Cnpj)item.Category="Emitidas";else if(dest==Cnpj)item.Category="Recebidas";else item.Category="Outras autorizadas";
    var partner=info.Element(n+(item.Category=="Emitidas"?"dest":"emit"));item.Partner=Rules.Child(partner,"xNome");item.Cnpj=Rules.Child(partner,"CNPJ");if(item.Cnpj=="")item.Cnpj=Rules.Child(partner,"CPF");
    item.Number=Rules.Child(ide,"nNF");item.Date=Rules.Child(ide,"dhEmi");if(item.Date=="")item.Date=Rules.Child(ide,"dEmi");
    var total=info.Element(n+"total");string amount=Rules.Child(total==null?null:total.Element(n+"ICMSTot"),"vNF");decimal a;decimal.TryParse(amount,NumberStyles.Number,CultureInfo.InvariantCulture,out a);item.Amount=a;
    item.Status=Rules.Child(prot,"cStat")=="100"||Rules.Child(prot,"cStat")=="150"?"XML completo / autorizado":"Protocolo "+Rules.Child(prot,"cStat");
   } else if(root.Name.LocalName=="resNFe") {
    item.Category="Resumos";item.Key=Rules.Child(root,"chNFe");if(!Rules.Key(item.Key))throw new InvalidDataException("Resumo com chave inválida.");item.Date=Rules.Child(root,"dhEmi");item.Partner=Rules.Child(root,"xNome");item.Cnpj=Rules.Child(root,"CNPJ");decimal a;decimal.TryParse(Rules.Child(root,"vNF"),NumberStyles.Number,CultureInfo.InvariantCulture,out a);item.Amount=a;item.Status="Resumo: XML completo pode exigir manifestação";
   } else if(root.Name.LocalName=="procEventoNFe"||root.Name.LocalName=="resEvento") {
    item.Category="Eventos";var info=root.Descendants(n+"infEvento").FirstOrDefault();item.Key=Rules.Child(info??root,"chNFe");item.Date=Rules.Child(info??root,"dhEvento");item.Status="Evento "+Rules.Child(info??root,"tpEvento");item.Partner=Rules.Child(info??root,"xNome");
   }if(item.Category=="Resumos"){item.NoteSituation=Rules.Child(root,"cSitNFe");if(item.Cnpj=="")item.Cnpj=Rules.Child(root,"CPF");}
   if(item.Category=="Eventos"){var eventInfo=root.Descendants(n+"infEvento").FirstOrDefault()??root;item.EventType=Rules.Child(eventInfo,"tpEvento");item.Cnpj=Rules.Child(eventInfo,"CNPJ");}
   return item;
  }
  public DocItem Store(byte[] data,string nsu,bool imported=false) {
   var item=Describe(data);item.Nsu=nsu;string id=Rules.Key(item.Key)?item.Key:Rules.Hash(data).Substring(0,24);
   string name=item.Category=="Eventos"||item.Category=="Outros"?id+"_"+Rules.Hash(data).Substring(0,16):id;
   string path=Path.Combine(Folder,item.Category,name+".xml");
   if(imported&&item.Category!="Emitidas"&&item.Category!="Recebidas")throw new InvalidDataException("Importe uma NF-e completa (nfeProc), emitida ou recebida por este CNPJ.");
   if(File.Exists(path)) {byte[] previous=File.ReadAllBytes(path);if(!previous.SequenceEqual(data)){path=Path.Combine(Folder,item.Category,name+"_"+Rules.Hash(data).Substring(0,16)+".xml");}}
   if(!File.Exists(path))Rules.WriteAtomic(path,data);item.FilePath=path;return item;
  }
  public List<DocItem> List() {
   var list=new List<DocItem>();if(!Directory.Exists(Folder))return list;
   foreach(string f in Directory.EnumerateFiles(Folder,"*.xml",SearchOption.AllDirectories)) {
    try {var item=Describe(File.ReadAllBytes(f),f);if(item.Category!="Outros")list.Add(item);}catch(Exception e){if(!(e is IOException)&&!(e is XmlException)&&!(e is InvalidDataException))throw;}
   }var cancelled=new HashSet<string>(list.Where(x=>x.Category=="Eventos"&&x.Status=="Evento 110111").Select(x=>x.Key));foreach(var item in list)if((item.Category=="Recebidas"||item.Category=="Emitidas")&&cancelled.Contains(item.Key))item.Status="XML completo / evento de cancelamento";return list.OrderByDescending(x=>x.Date).ToList();
  }
 }
 public sealed class CompressedDoc {public string Nsu;public string Schema;public byte[] Bytes;}
 public sealed class DistributionResult {
  public string Code,Motive,LastNsu,MaxNsu;public List<CompressedDoc> Docs=new List<CompressedDoc>();
  public static DistributionResult Parse(byte[] bytes) {
   var xml=Rules.Xml(bytes);XNamespace n=Rules.NfeNs;var root=xml.Descendants(n+"retDistDFeInt").FirstOrDefault();
   if(root==null)throw new InvalidDataException("A SEFAZ não retornou retDistDFeInt. Confira a conexão e tente mais tarde.");
   var ret=new DistributionResult{Code=Rules.Child(root,"cStat"),Motive=Rules.Child(root,"xMotivo"),LastNsu=Rules.Child(root,"ultNSU"),MaxNsu=Rules.Child(root,"maxNSU")};
   if(ret.LastNsu!="")ret.LastNsu=Rules.Nsu(ret.LastNsu);if(ret.MaxNsu!="")ret.MaxNsu=Rules.Nsu(ret.MaxNsu);
   foreach(var packed in root.Descendants(n+"docZip")) {
    if(ret.Docs.Count>=50)throw new InvalidDataException("Lote de distribuição acima de 50 documentos.");
    if(packed.Value.Length>8*1024*1024)throw new InvalidDataException("Documento compactado acima do limite.");byte[] zipped=Convert.FromBase64String(packed.Value),data;
    using(var input=new MemoryStream(zipped))using(var gzip=new GZipStream(input,CompressionMode.Decompress))using(var output=new MemoryStream()) {byte[] buf=new byte[8192];int count;while((count=gzip.Read(buf,0,buf.Length))>0){if(output.Length+count>20*1024*1024)throw new InvalidDataException("XML descompactado acima do limite.");output.Write(buf,0,count);}data=output.ToArray();}
    ret.Docs.Add(new CompressedDoc{Nsu=(string)packed.Attribute("NSU")??"",Schema=(string)packed.Attribute("schema")??"",Bytes=data});
   }return ret;
  }
 }
 /// <summary>Cursor NSU e pausas por empresa/ambiente; consultas por chave não avançam Last.</summary>
 public sealed class CursorState {
  public string Last="000000000000000",Max="000000000000000";public bool Initialized;public DateTime DistAfter=DateTime.MinValue,AllAfter=DateTime.MinValue;public List<DateTime> KeyQueries=new List<DateTime>();
  public void Apply(DistributionResult result,bool byKey,DateTime now) {
   if(result.Code=="656") {
    AllAfter=now.AddHours(1);DistAfter=AllAfter;
    if(!byKey&&result.LastNsu!=""&&String.CompareOrdinal(result.LastNsu,Last)>0){Last=result.LastNsu;Initialized=true;}return;
   }
   if(result.Code!="137"&&result.Code!="138")return;
   if(!byKey) {
    if(result.LastNsu=="" || result.MaxNsu=="")throw new InvalidDataException("Distribuição sem NSUs: não é seguro continuar.");
    if(String.CompareOrdinal(result.LastNsu,Last)<0 || String.CompareOrdinal(result.LastNsu,result.MaxNsu)>0)throw new InvalidDataException("A SEFAZ retornou uma sequência de NSUs inconsistente.");
    if(result.Code=="138"&&result.LastNsu==Last&&String.CompareOrdinal(result.MaxNsu,Last)>0)throw new InvalidDataException("Lote sem avanço do NSU. Busca interrompida para evitar repetição.");
    Last=result.LastNsu;Max=result.MaxNsu;Initialized=true;
    if(result.Code=="137"||Last==Max)DistAfter=now.AddHours(1);
   }
  }
 }
 public sealed class StateStore {
  public string BaseFolder;public StateStore(string folder=null){BaseFolder=folder??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"XML-A1","estado");Directory.CreateDirectory(BaseFolder);}
  string PathFor(string cnpj,int env){return Path.Combine(BaseFolder,Rules.Cnpj(cnpj)+"_"+env+".xml");}
  public FileStream Lock(string cnpj,int env){try{return new FileStream(PathFor(cnpj,env)+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new InvalidOperationException("Já existe uma busca em andamento para este CNPJ e ambiente.");}}
  public CursorState Load(string cnpj,int env) {
   string path=PathFor(cnpj,env);var s=new CursorState();if(!File.Exists(path))return s;var xml=Rules.Xml(File.ReadAllBytes(path));var e=xml.Root;
   try {s.Last=Rules.Nsu(e.Element("ultimoNSU").Value);s.Max=Rules.Nsu(e.Element("maxNSU").Value);s.Initialized=bool.Parse(e.Element("iniciado").Value);s.DistAfter=Date(e.Element("proximaDistribuicao"));s.AllAfter=Date(e.Element("bloqueadoAte"));foreach(var x in e.Elements("consultaChave"))s.KeyQueries.Add(Date(x));return s;}catch(Exception ex){throw new InvalidDataException("Estado de consultas inválido. Preserve o arquivo e recupere o NSU anterior antes de consultar.",ex);}
  }
  static DateTime Date(XElement e){return DateTime.Parse(e.Value,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind).ToUniversalTime();}
  public void Save(string cnpj,int env,CursorState s) {
   var xml=new XDocument(new XElement("estado",new XElement("ultimoNSU",s.Last),new XElement("maxNSU",s.Max),new XElement("iniciado",s.Initialized),new XElement("proximaDistribuicao",s.DistAfter.ToUniversalTime().ToString("o")),new XElement("bloqueadoAte",s.AllAfter.ToUniversalTime().ToString("o")),s.KeyQueries.Where(t=>t>DateTime.UtcNow.AddHours(-1)).Select(t=>new XElement("consultaChave",t.ToString("o")))));
   Rules.WriteAtomic(PathFor(cnpj,env),Rules.Utf8(xml));
  }
 }
 /// <summary>Fronteira de transporte substituída por respostas fictícias nos testes offline.</summary>
 public interface ISefaz {byte[] Distribute(string cnpj,int uf,int env,string nsu,string key,CancellationToken token);byte[] Science(string cnpj,int env,string key,CancellationToken token);}
 public sealed class SefazClient:ISefaz {
  readonly X509Certificate2 cert;public Action<byte[]> ScienceRequest;public SefazClient(X509Certificate2 certificate){cert=certificate;}
  public static byte[] DistributionEnvelope(string cnpj,int uf,int env,string nsu,string key) {
   XNamespace n=Rules.NfeNs,w="http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe",s="http://www.w3.org/2003/05/soap-envelope";
   var request=new XElement(n+"distDFeInt",new XAttribute("versao","1.01"),new XElement(n+"tpAmb",env),new XElement(n+"cUFAutor",uf),new XElement(n+"CNPJ",Rules.Cnpj(cnpj)),key==null?new XElement(n+"distNSU",new XElement(n+"ultNSU",Rules.Nsu(nsu))):new XElement(n+"consChNFe",new XElement(n+"chNFe",key)));
   return Rules.Utf8(new XDocument(new XElement(s+"Envelope",new XAttribute(XNamespace.Xmlns+"soap",s),new XElement(s+"Body",new XElement(w+"nfeDistDFeInteresse",new XElement(w+"nfeDadosMsg",request))))));
  }
  public byte[] Distribute(string cnpj,int uf,int env,string nsu,string key,CancellationToken token){if(key!=null&&!Rules.Key(key))throw new InvalidOperationException("Chave de NF-e inválida.");return Post(env==1?"https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx":"https://hom1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx","http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe/nfeDistDFeInteresse",DistributionEnvelope(cnpj,uf,env,nsu,key),token);}
  public static XmlDocument ScienceDocument(string cnpj,int env,string key,X509Certificate2 certificate) {
   if(!Rules.Key(key))throw new InvalidOperationException("Chave de NF-e inválida.");XNamespace n=Rules.NfeNs;string id="ID210210"+key+"01";
   var payload=new XDocument(new XElement(n+"envEvento",new XAttribute("versao","1.00"),new XElement(n+"idLote",DateTime.UtcNow.ToString("yyyyMMddHHmmss")),new XElement(n+"evento",new XAttribute("versao","1.00"),new XElement(n+"infEvento",new XAttribute("Id",id),new XElement(n+"cOrgao","91"),new XElement(n+"tpAmb",env),new XElement(n+"CNPJ",Rules.Cnpj(cnpj)),new XElement(n+"chNFe",key),new XElement(n+"dhEvento",DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),new XElement(n+"tpEvento","210210"),new XElement(n+"nSeqEvento","1"),new XElement(n+"verEvento","1.00"),new XElement(n+"detEvento",new XAttribute("versao","1.00"),new XElement(n+"descEvento","Ciencia da Operacao"))))));
   var xml=new XmlDocument{PreserveWhitespace=true,XmlResolver=null};xml.LoadXml(payload.ToString(SaveOptions.DisableFormatting));
   using(var rsa=certificate.GetRSAPrivateKey()) {
    var signed=new SignedXml(xml){SigningKey=rsa};signed.SignedInfo.CanonicalizationMethod=SignedXml.XmlDsigC14NTransformUrl;signed.SignedInfo.SignatureMethod=SignedXml.XmlDsigRSASHA1Url;
    var reference=new Reference("#"+id){DigestMethod=SignedXml.XmlDsigSHA1Url};reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());reference.AddTransform(new XmlDsigC14NTransform());signed.AddReference(reference);var keyInfo=new KeyInfo();keyInfo.AddClause(new KeyInfoX509Data(certificate));signed.KeyInfo=keyInfo;signed.ComputeSignature();xml.GetElementsByTagName("evento",Rules.NfeNs)[0].AppendChild(xml.ImportNode(signed.GetXml(),true));
   }return xml;
  }
  public static byte[] ScienceEnvelope(XmlDocument payload) {
   XNamespace s="http://www.w3.org/2003/05/soap-envelope",w="http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4";
   // Avoid adding inherited prefix declarations to the inclusively canonicalized signature.
   return Rules.Utf8(new XDocument(new XElement(s+"Envelope",new XElement(s+"Body",new XElement(w+"nfeDadosMsg",XElement.Parse(payload.OuterXml,LoadOptions.PreserveWhitespace))))));
  }
  public byte[] Science(string cnpj,int env,string key,CancellationToken token) {
   byte[] body=ScienceEnvelope(ScienceDocument(cnpj,env,key,cert));if(ScienceRequest!=null)ScienceRequest(body);return Post(env==1?"https://www.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx":"https://hom1.nfe.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx","http://www.portalfiscal.inf.br/nfe/wsdl/NFeRecepcaoEvento4/nfeRecepcaoEventoNF",body,token);
  }
  byte[] Post(string url,string action,byte[] body,CancellationToken token) {
   token.ThrowIfCancellationRequested();ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;ServicePointManager.CheckCertificateRevocationList=true;
   var request=(HttpWebRequest)WebRequest.Create(url);request.Method="POST";request.AllowAutoRedirect=false;request.ContentType="application/soap+xml; charset=utf-8; action=\""+action+"\"";request.Timeout=60000;request.ReadWriteTimeout=60000;request.ClientCertificates.Add(cert);request.ContentLength=body.Length;request.KeepAlive=false;request.UserAgent="XML-A1/1.0";
   using(token.Register(request.Abort)) {
    try {using(var output=request.GetRequestStream())output.Write(body,0,body.Length);using(var response=(HttpWebResponse)request.GetResponse()){if(response.StatusCode!=HttpStatusCode.OK)throw new IOException("Resposta HTTP inesperada da SEFAZ.");using(var input=response.GetResponseStream())using(var result=new MemoryStream()){byte[] buf=new byte[8192];int count;while((count=input.Read(buf,0,buf.Length))>0){if(result.Length+count>20*1024*1024)throw new InvalidDataException("Resposta da SEFAZ acima do limite.");result.Write(buf,0,count);}return result.ToArray();}}}
    catch(WebException ex){if(token.IsCancellationRequested)throw new OperationCanceledException(token);throw new IOException("Falha na conexão com a SEFAZ ("+ex.Status+"). Verifique internet, certificado e cadeia ICP-Brasil no Windows.",ex);}
   }
  }
 }
 /// <summary>Consulta, salva todo o lote e só depois confirma o cursor persistente.</summary>
 public sealed class Downloader {
  public DistributionResult LastResult{get;private set;}
  readonly StateStore states;readonly ISefaz service;readonly XmlLibrary library;readonly Action<string> log;
  public Downloader(StateStore state,ISefaz client,XmlLibrary lib,Action<string> logger){states=state;service=client;library=lib;log=logger??delegate{};}
  public int Run(int uf,string initialNsu,string key,CancellationToken token) {
   using(states.Lock(library.Cnpj,library.Environment)) {
    var state=states.Load(library.Cnpj,library.Environment);bool byKey=key!=null;var now=DateTime.UtcNow;
    if(now<state.AllAfter || (!byKey&&now<state.DistAfter))throw new InvalidOperationException("Próxima consulta permitida após "+(now<state.AllAfter?state.AllAfter:state.DistAfter).ToLocalTime().ToString("dd/MM HH:mm")+".");
    if(byKey&&!Rules.Key(key))throw new InvalidOperationException("Informe uma chave válida de NF-e modelo 55.");
    if(!byKey&&!state.Initialized&&initialNsu!=""){state.Last=Rules.Nsu(initialNsu);states.Save(library.Cnpj,library.Environment,state);}
    int total=0;
    for(int batch=0;batch<100;batch++) {
     token.ThrowIfCancellationRequested();now=DateTime.UtcNow;
     DateTime previousBlock=state.AllAfter;
     if(byKey) {state.KeyQueries.RemoveAll(t=>t<=now.AddHours(-1));if(state.KeyQueries.Count>=20)throw new InvalidOperationException("Limite de 20 consultas por chave por hora atingido.");state.KeyQueries.Add(now);state.AllAfter=now.AddHours(1);states.Save(library.Cnpj,library.Environment,state);}
     DistributionResult result;
     try {
      log(byKey?"Consultando a chave informada...":"Consultando a partir do NSU "+state.Last+"...");
      result=DistributionResult.Parse(service.Distribute(library.Cnpj,uf,library.Environment,state.Last,key,token));LastResult=result;
      if(result.Code=="138"){if(result.Docs.Count==0)throw new InvalidDataException("A SEFAZ informou documentos, mas o lote está vazio.");foreach(var doc in result.Docs){library.Store(doc.Bytes,doc.Nsu);total++;}}
      if(byKey)state.AllAfter=previousBlock;state.Apply(result,byKey,DateTime.UtcNow);states.Save(library.Cnpj,library.Environment,state);
     }catch {state.AllAfter=DateTime.UtcNow.AddHours(1);states.Save(library.Cnpj,library.Environment,state);throw;}
     log("SEFAZ "+result.Code+": "+result.Motive+" | "+result.Docs.Count+" documento(s).");
     if(result.Code=="656"){log("Consultas suspensas por uma hora. NSU alinhado ao retorno quando informado; verifique outros buscadores deste CNPJ.");break;}
     if(result.Code!="137"&&result.Code!="138")throw new InvalidOperationException("SEFAZ "+result.Code+": "+result.Motive);
     if(byKey||result.Code=="137"||state.Last==state.Max)break;
     if(batch==99)log("Limite de 100 lotes por execução. Próxima busca continuará do NSU gravado.");
    }return total;
   }
  }
 }
}
