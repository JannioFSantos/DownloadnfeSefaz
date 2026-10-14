using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

namespace XmlA1 {
 /// <summary>Extrai chaves completas de planilha sem tentar recuperar dígitos perdidos.</summary>
 public sealed class KeyPaste {
  public List<string> Keys=new List<string>();public List<string> Invalid=new List<string>();public int Duplicates,Ignored,Scientific;
  public static KeyPaste Parse(string input) {
   if((input??"").Length>2*1024*1024)throw new InvalidOperationException("Cole até 2 MB de texto por vez.");
   var result=new KeyPaste();var seen=new HashSet<string>();
   foreach(string raw in Regex.Split((input??"").ToUpperInvariant(),@"[\r\n\t;,]+")) {
    string cell=raw.Trim().Trim('"','\'').Trim();if(cell=="")continue;
    if(Regex.IsMatch(cell,@"^=?[0-9]+[.,]?[0-9]*E[+-]?[0-9]+$")){result.Scientific++;result.Invalid.Add("Notação científica: "+cell);continue;}
    var candidates=Regex.Matches(cell,@"(?<![A-Z0-9])(?:NFE)?([0-9]{6}[A-Z0-9]{12}[0-9]{26})(?![A-Z0-9])").Cast<Match>().Select(m=>m.Groups[1].Value).ToList();
    if(candidates.Count==0){string compact=Regex.Replace(cell,@"\s+","");if(compact.StartsWith("NFE"))compact=compact.Substring(3);if(Regex.IsMatch(compact,@"^[A-Z0-9]{44}$"))candidates.Add(compact);else{result.Ignored++;continue;}}
    foreach(string key in candidates) {
     if(!Rules.Key(key)){result.Invalid.Add(key+" — chave inválida ou modelo diferente de NF-e 55");continue;}
     if(!seen.Add(key)){result.Duplicates++;continue;}result.Keys.Add(key);
     if(result.Keys.Count>10000)throw new InvalidOperationException("Limite de 10.000 chaves por colagem.");
    }
   }return result;
  }
 }
 public static class DocumentFilter {
  public static bool TryDate(string value,out DateTime day) {
   DateTimeOffset dto;if(DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out dto)){day=dto.Date;return true;}day=DateTime.MinValue;return false;
  }
  public static List<DocItem> Apply(IEnumerable<DocItem> docs,string category,string query,DateTime? from,DateTime? to) {
   if(from.HasValue&&to.HasValue&&from.Value.Date>to.Value.Date)throw new InvalidOperationException("A data inicial deve ser anterior ou igual à final.");string q=(query??"").Trim();
   return docs.Where(d=>d.Category==category&&(q==""||(d.Key+" "+d.Partner+" "+d.Number+" "+d.Cnpj).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)&&InRange(d.Date,from,to)).ToList();
  }
  static bool InRange(string value,DateTime? from,DateTime? to){if(!from.HasValue&&!to.HasValue)return true;DateTime day;if(!TryDate(value,out day))return false;return (!from.HasValue||day>=from.Value.Date)&&(!to.HasValue||day<=to.Value.Date);}
 }
 public sealed class KeyJob {
  public string Key{get;set;}public string Status{get;set;}public string Code{get;set;}public string Message{get;set;}public int Attempts{get;set;}public DateTime Updated{get;set;}public bool AwaitXml{get;set;}public int XmlChecks{get;set;}public DateTime NotBefore{get;set;}public string ScienceCode{get;set;}public bool UserSupplied{get;set;}public string ConfirmationCode{get;set;}
 }
 /// <summary>Fila persistida; autorização de ciência é independente da retomada de downloads.</summary>
 public sealed class KeyQueue {
  public List<KeyJob> Jobs=new List<KeyJob>();public bool AutoContinue,DownloadAll,ScienceForDownload,ConfirmForDownload;public DateTime ScienceConsent,ConfirmationConsent;public int Uf;public DateTime Next=DateTime.MinValue;
  public int Pending{get{return Jobs.Count(x=>x.Status=="Pendente"||x.Status=="Consultando");}}
  public int Add(IEnumerable<string> keys){int added=0;var known=new HashSet<string>(Jobs.Select(x=>x.Key));foreach(var key in keys){if(!Rules.Key(key))throw new InvalidDataException("Chave inválida na fila.");if(known.Add(key)){if(Jobs.Count>=10000)throw new InvalidOperationException("A fila possui 10.000 chaves. Exporte os resultados e crie outra lista.");Jobs.Add(new KeyJob{Key=key,Status="Pendente",Code="",Message="",AwaitXml=DownloadAll,Updated=DateTime.UtcNow});added++;}}return added;}
  public int StartDownloads(){DownloadAll=true;int restored=0;foreach(var job in Jobs){if(new[]{"Resumo / eventos","Necessita manifestação","Não encontrada","XML indisponível"}.Contains(job.Status)){job.Status="Pendente";job.NotBefore=DateTime.MinValue;job.XmlChecks=0;restored++;}if(job.Status=="Pendente"||job.Status=="Consultando")job.AwaitXml=true;}return restored;}
  // Apenas a entrada explícita do usuário autoriza uma chave para confirmação.
  // Add é usado por tarefas internas e permanece sem essa marca.
  public int AddUserKeys(IEnumerable<string> keys){var supplied=keys.ToList();int added=Add(supplied);var known=new HashSet<string>(supplied);foreach(var job in Jobs.Where(j=>known.Contains(j.Key)))job.UserSupplied=true;return added;}
 }
 public sealed class KeyQueueStore {
  public string Folder;public KeyQueueStore(string folder=null){Folder=folder??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"XML-A1","filas");Directory.CreateDirectory(Folder);}
  string FileName(string cnpj,int env){return Path.Combine(Folder,Rules.Cnpj(cnpj)+"_"+env+".xml");}
  public FileStream Lock(string cnpj,int env){try{return new FileStream(FileName(cnpj,env)+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){throw new InvalidOperationException("A fila deste CNPJ já está em processamento em outra janela.");}}
  public KeyQueue Load(string cnpj,int env) {
   string path=FileName(cnpj,env);var q=new KeyQueue();if(!File.Exists(path))return q;
   var root=Rules.Xml(File.ReadAllBytes(path)).Root;
   try {
    q.ConfirmForDownload=(bool?)root.Attribute("confirmarNaFila")??false;q.ConfirmationConsent=DateTime.Parse((string)root.Attribute("confirmacaoAutorizada")??DateTime.MinValue.ToString("o"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);q.DownloadAll=(bool?)root.Attribute("baixarTodas")??false;q.ScienceForDownload=(bool?)root.Attribute("cienciaNaFila")??false;q.ScienceConsent=DateTime.Parse((string)root.Attribute("cienciaAutorizada")??DateTime.MinValue.ToString("o"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);q.AutoContinue=(bool?)root.Attribute("automatica")??false;q.Uf=(int?)root.Attribute("uf")??0;q.Next=DateTime.Parse((string)root.Attribute("proxima")??DateTime.MinValue.ToString("o"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
    var known=new HashSet<string>();foreach(var node in root.Elements("chave")){if(q.Jobs.Count>=10000)throw new InvalidDataException("Fila acima do limite.");string key=(string)node.Attribute("valor");if(!Rules.Key(key)||!known.Add(key))throw new InvalidDataException("Chave inválida ou repetida na fila.");var job=new KeyJob{Key=key,Status=(string)node.Attribute("situacao"),Code=(string)node.Attribute("codigo")??"",Attempts=(int?)node.Attribute("tentativas")??0,Message=node.Value,Updated=DateTime.Parse((string)node.Attribute("atualizada"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind),UserSupplied=(bool?)node.Attribute("fornecidaPeloUsuario")??false,ConfirmationCode=(string)node.Attribute("confirmacaoCodigo")??"",ScienceCode=(string)node.Attribute("cienciaCodigo")??"",AwaitXml=(bool?)node.Attribute("aguardaXml")??false,XmlChecks=(int?)node.Attribute("consultasXml")??0,NotBefore=DateTime.Parse((string)node.Attribute("naoAntes")??DateTime.MinValue.ToString("o"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind)};if(job.Status=="Consultando"){job.Status="Pendente";job.Message="Consulta anterior interrompida; será retomada respeitando o limite.";}q.Jobs.Add(job);}return q;
   }catch(Exception ex){throw new InvalidDataException("A fila salva está inválida. Preserve o arquivo para recuperação.",ex);}
  }
  public void Save(string cnpj,int env,KeyQueue q) {
   var xml=new XDocument(new XElement("fila",new XAttribute("confirmarNaFila",q.ConfirmForDownload),new XAttribute("confirmacaoAutorizada",q.ConfirmationConsent.ToString("o")),new XAttribute("baixarTodas",q.DownloadAll),new XAttribute("cienciaNaFila",q.ScienceForDownload),new XAttribute("cienciaAutorizada",q.ScienceConsent.ToString("o")),new XAttribute("automatica",q.AutoContinue),new XAttribute("uf",q.Uf),new XAttribute("proxima",q.Next.ToString("o")),q.Jobs.Select(x=>new XElement("chave",new XAttribute("valor",x.Key),new XAttribute("situacao",x.Status),new XAttribute("codigo",x.Code??""),new XAttribute("tentativas",x.Attempts),new XAttribute("atualizada",x.Updated.ToString("o")),new XAttribute("fornecidaPeloUsuario",x.UserSupplied),new XAttribute("confirmacaoCodigo",x.ConfirmationCode??""),new XAttribute("cienciaCodigo",x.ScienceCode??""),new XAttribute("aguardaXml",x.AwaitXml),new XAttribute("consultasXml",x.XmlChecks),new XAttribute("naoAntes",x.NotBefore.ToString("o")),x.Message??""))));Rules.WriteAtomic(FileName(cnpj,env),Rules.Utf8(xml));
  }
 }
 public sealed class BatchSummary {public int Processed,Downloaded,Pending;public DateTime Next;public string Message="";}
 /// <summary>Executa consultas sob limite compartilhado, priorizando chaves ainda não verificadas.</summary>
 public sealed class KeyBatchRunner {
  readonly StateStore states;readonly KeyQueueStore queues;readonly XmlLibrary library;readonly ISefaz client;readonly Action<string> log;readonly Func<DateTime> clock;
  public KeyBatchRunner(StateStore s,KeyQueueStore q,XmlLibrary l,ISefaz c,Action<string> logger,Func<DateTime> utcNow=null){states=s;queues=q;library=l;client=c;log=logger??delegate{};clock=utcNow??(()=>DateTime.UtcNow);}
  public static DateTime AllowedAfter(CursorState state,DateTime now) {var recent=state.KeyQueries.Where(t=>t>now.AddHours(-1)).OrderBy(t=>t).ToList();DateTime date=state.AllAfter;if(recent.Count>=20){DateTime slot=recent[recent.Count-20].AddHours(1).AddSeconds(1);if(slot>date)date=slot;}return date;}
  public BatchSummary Run(CancellationToken token) {
   using(queues.Lock(library.Cnpj,library.Environment)) {
    var q=queues.Load(library.Cnpj,library.Environment);var result=new BatchSummary();if(q.Uf==0)throw new InvalidOperationException("Selecione a UF antes de iniciar a fila.");
    var available=new HashSet<string>(library.List().Where(d=>d.Category=="Recebidas"||d.Category=="Emitidas"||d.Category=="Outras autorizadas").Select(d=>d.Key));
    foreach(var job in q.Jobs.Where(j=>j.Status=="Pendente").OrderBy(j=>j.XmlChecks).ThenBy(j=>j.NotBefore).ThenBy(j=>j.Updated).ToList()) {
     if(token.IsCancellationRequested){q.AutoContinue=false;result.Message="Fila pausada. As pendentes foram preservadas.";break;}
     if(available.Contains(job.Key)){job.Status="Já disponível";job.Message="XML completo já está na biblioteca; consulta dispensada.";job.Updated=clock();queues.Save(library.Cnpj,library.Environment,q);continue;}
     if(job.NotBefore>clock())continue;
     var state=states.Load(library.Cnpj,library.Environment);var allowed=AllowedAfter(state,clock());if(allowed>clock()){q.Next=allowed;result.Next=allowed;result.Message="Fila aguardando a próxima janela de consulta.";break;}
     job.Status="Consultando";job.Updated=clock();job.Attempts++;queues.Save(library.Cnpj,library.Environment,q);
     var downloader=new Downloader(states,client,library,log);
     try {
      downloader.Run(q.Uf,"",job.Key,token);var response=downloader.LastResult;if(response==null)throw new IOException("Consulta sem resultado.");job.Code=response.Code;job.Message=response.Motive;result.Processed++;
      if(response.Code=="656"){job.Status="Pendente";q.Next=states.Load(library.Cnpj,library.Environment).AllAfter;result.Next=q.Next;result.Message="SEFAZ 656: fila pausada por uma hora.";queues.Save(library.Cnpj,library.Environment,q);break;}
      if(response.Code=="137")job.Status="Não encontrada";
      else {bool complete=response.Docs.Any(d=>{var item=library.Describe(d.Bytes);return item.Key==job.Key&&(item.Category=="Recebidas"||item.Category=="Emitidas"||item.Category=="Outras autorizadas");});job.Status=complete?"Baixada":"Resumo / eventos";if(complete){available.Add(job.Key);result.Downloaded++;}}
      if(job.AwaitXml&&(job.Status=="Não encontrada"||job.Status=="Resumo / eventos")){job.XmlChecks++;bool ended=!q.DownloadAll&&job.XmlChecks>=24;job.NotBefore=clock().AddHours(q.DownloadAll&&job.XmlChecks>=24?6:1);job.Status=ended?"XML indisponível":"Pendente";string manifestation=!String.IsNullOrEmpty(job.ConfirmationCode)?"Confirmação "+job.ConfirmationCode+"; ":job.ScienceCode=="596"?"Ciência rejeitada por prazo (596); ":new[]{"135","136","573"}.Contains(job.ScienceCode)?"Ciência "+job.ScienceCode+"; ":"";job.Message=manifestation+"XML completo ainda não disponibilizado. "+(ended?"24 consultas realizadas; revise e continue manualmente.":"Nova tentativa após "+job.NotBefore.ToLocalTime().ToString("dd/MM HH:mm"));}
      if(job.ScienceCode=="596"&&!job.AwaitXml){if(job.Status=="Resumo / eventos"){job.Status="Necessita manifestação";job.Message="SEFAZ devolveu apenas resumo/eventos. Ciência rejeitada por prazo (596), sem registro de ciência aceito nesta tentativa. Confira uma manifestação válida ou obtenha o XML com o fornecedor.";}else if(job.Status=="Não encontrada")job.Message="XML não disponibilizado na consulta por chave. A rejeição 596 não registra ciência. Confira disponibilidade e permissão na SEFAZ.";else if(job.Status=="Baixada")job.Message="XML completo disponível na SEFAZ e salvo. Não foi necessário registrar nova ciência; a tentativa anterior permaneceu rejeitada (596).";}
     }catch(Exception ex) {
      var response=downloader.LastResult;job.Message=ex.Message;job.Code=response==null?"":response.Code;
      if(response!=null&&response.Code!="137"&&response.Code!="138"&&response.Code!="656") {
       job.Status="Rejeitada";result.Processed++;
       if(new[]{"108","109","280","281","282","283","284","285","286","473","593","999"}.Contains(response.Code)){job.Status="Pendente";q.AutoContinue=false;result.Message="Fila pausada: confira o certificado ou a disponibilidade da SEFAZ.";queues.Save(library.Cnpj,library.Environment,q);break;}
      } else {job.Status="Pendente";q.Next=states.Load(library.Cnpj,library.Environment).AllAfter;result.Next=q.Next;if(token.IsCancellationRequested)q.AutoContinue=false;result.Message="Fila pausada: "+ex.Message;queues.Save(library.Cnpj,library.Environment,q);break;}
     }
     job.Updated=clock();queues.Save(library.Cnpj,library.Environment,q);log("Chave "+job.Key+": "+job.Status+" ("+job.Code+").");
    }
    result.Pending=q.Pending;if(result.Pending==0){q.Next=DateTime.MinValue;q.AutoContinue=false;result.Message="Fila concluída.";}else{var due=q.Jobs.Where(j=>j.Status=="Pendente").Select(j=>j.NotBefore).DefaultIfEmpty(DateTime.MinValue).Min();var allowed=AllowedAfter(states.Load(library.Cnpj,library.Environment),clock());q.Next=due>allowed?due:allowed;result.Next=q.Next;if(result.Message=="")result.Message="Fila aguardando liberação do XML.";}queues.Save(library.Cnpj,library.Environment,q);return result;
   }
  }
 }
}
