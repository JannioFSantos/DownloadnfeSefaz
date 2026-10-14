using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace XmlA1 {
 /// <summary>Histórico exclusivo de confirmações, separado das tentativas de ciência.</summary>
 public sealed class ConfirmationStore {
  readonly ScienceStore inner;
  public ConfirmationStore(string folder=null){inner=new ScienceStore(folder??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"XML-A1","confirmacoes"),"confirmacoes");}
  public FileStream Lock(string cnpj,int env){return inner.Lock(cnpj,env);}
  public ScienceState Load(string cnpj,int env){return inner.Load(cnpj,env);}
  public void Save(string cnpj,int env,ScienceState state){inner.Save(cnpj,env,state);}
 }

 /// <summary>
 /// Confirma somente operações reconhecidas na entrada manual da fila e autorizadas ao iniciar.
 /// Não usa a pasta de resumos como autorização e nunca envia uma sequência de retificação.
 /// </summary>
 public sealed class AutoConfirmationRunner {
  readonly StateStore states;readonly ConfirmationStore history;readonly KeyQueueStore queues;
  readonly XmlLibrary library;readonly IConfirmationSefaz client;readonly Action<string> log;readonly Func<DateTime> now;
  public AutoConfirmationRunner(StateStore states,ConfirmationStore history,KeyQueueStore queues,XmlLibrary library,IConfirmationSefaz client,Action<string> logger,Func<DateTime> clock=null){this.states=states;this.history=history;this.queues=queues;this.library=library;this.client=client;log=logger??delegate{};now=clock??(()=>DateTime.UtcNow);}

  public static bool Eligible(DocItem item,string cnpj,IEnumerable<DocItem> docs){return item.Category=="Resumos"&&item.NoteSituation=="1"&&Rules.Key(item.Key)&&!String.IsNullOrEmpty(item.Cnpj)&&item.Cnpj!=cnpj&&item.Key.Substring(6,14)==item.Cnpj.PadLeft(14,'0')&&!docs.Any(d=>d.Key==item.Key&&(d.Category=="Recebidas"||d.Category=="Emitidas"||d.Category=="Outras autorizadas"||(d.Category=="Resumos"&&(d.NoteSituation=="2"||d.NoteSituation=="3"))||(d.Category=="Eventos"&&new[]{"110111","210220","210240"}.Contains(d.EventType))));}
  public static ScienceRecord Parse(byte[] bytes,string cnpj,int env,string key){return AutoScienceRunner.ParseEvent(bytes,cnpj,env,key,"210200");}
  public bool HasPendingWork(){var q=queues.Load(library.Cnpj,library.Environment);if(!q.ConfirmForDownload||q.ConfirmationConsent==DateTime.MinValue)return false;var state=history.Load(library.Cnpj,library.Environment);if(state.Next>now()||states.Load(library.Cnpj,library.Environment).AllAfter>now())return false;var keys=new HashSet<string>(q.Jobs.Where(j=>j.UserSupplied&&j.Status=="Pendente"&&j.Attempts>0&&j.Code=="138").Select(j=>j.Key));if(state.Records.Any(r=>keys.Contains(r.Key)&&!r.Queued))return true;var docs=library.List();return docs.Any(d=>keys.Contains(d.Key)&&Eligible(d,library.Cnpj,docs)&&!state.Records.Any(r=>r.Key==d.Key));}

  void Pause(){using(queues.Lock(library.Cnpj,library.Environment)){var q=queues.Load(library.Cnpj,library.Environment);q.ConfirmForDownload=false;queues.Save(library.Cnpj,library.Environment,q);}}
  bool Authorized(string key){var q=queues.Load(library.Cnpj,library.Environment);return q.ConfirmForDownload&&q.ConfirmationConsent!=DateTime.MinValue&&q.Jobs.Any(j=>j.Key==key&&j.UserSupplied&&j.Status=="Pendente"&&j.Attempts>0&&j.Code=="138");}

  void UpdateQueue(ScienceRecord record){using(queues.Lock(library.Cnpj,library.Environment)){var q=queues.Load(library.Cnpj,library.Environment);var job=q.Jobs.SingleOrDefault(j=>j.Key==record.Key);if(job==null||!job.UserSupplied)return;job.ConfirmationCode=record.Code;job.Updated=now();
    if(record.Registered&&!record.Queued&&job.Status!="Baixada"&&job.Status!="Já disponível"){
     job.Status="Pendente";job.AwaitXml=true;job.XmlChecks=0;job.NotBefore=now().AddMinutes(5);
     job.Message="Confirmação "+record.Code+" ("+record.Status+"); aguardando disponibilização do XML completo.";
    }else if(!record.Registered){job.Message="Confirmação "+record.Code+": "+record.Message+". O evento não será repetido automaticamente.";}
    var due=q.Jobs.Where(j=>j.Status=="Pendente").Select(j=>j.NotBefore).DefaultIfEmpty(DateTime.MinValue).Min();var allowed=KeyBatchRunner.AllowedAfter(states.Load(library.Cnpj,library.Environment),now());q.Next=due>allowed?due:allowed;queues.Save(library.Cnpj,library.Environment,q);
   }record.Queued=true;}

  ScienceRecord ExistingEvent(string key,IEnumerable<DocItem> docs){XNamespace n=Rules.NfeNs;foreach(var item in docs.Where(d=>d.Category=="Eventos"&&d.EventType=="210200"&&d.Key==key&&d.Cnpj==library.Cnpj&&File.Exists(d.FilePath))){var x=Rules.Xml(File.ReadAllBytes(item.FilePath));var info=x.Descendants(n+"retEvento").Select(e=>e.Element(n+"infEvento")).FirstOrDefault(e=>e!=null&&Rules.Child(e,"cStat")=="135"&&Rules.Child(e,"chNFe")==key&&Rules.Child(e,"tpEvento")=="210200"&&Rules.Child(e,"tpAmb")==library.Environment.ToString());if(info!=null)return new ScienceRecord{Key=key,Status="Registrada",Code="135",At=now(),Message="Confirmação já registrada no evento distribuído; envio dispensado.",Protocol=Rules.Child(info,"nProt"),ResponsePath=item.FilePath};}return null;}

  public int Run(CancellationToken token){
   KeyQueue q;using(queues.Lock(library.Cnpj,library.Environment))q=queues.Load(library.Cnpj,library.Environment);
   if(!q.ConfirmForDownload||q.ConfirmationConsent==DateTime.MinValue)return 0;
   if(q.Uf==0)throw new InvalidOperationException("Selecione a UF antes de confirmar as operações da fila.");
   var keys=new HashSet<string>(q.Jobs.Where(j=>j.UserSupplied&&j.Status=="Pendente"&&j.Attempts>0&&j.Code=="138").Select(j=>j.Key));if(keys.Count==0)return 0;
   using(history.Lock(library.Cnpj,library.Environment)){
    var state=history.Load(library.Cnpj,library.Environment);if(state.Next>now()||states.Load(library.Cnpj,library.Environment).AllAfter>now())return 0;
    state.Consent=q.ConfirmationConsent;int sent=0;var docs=library.List();
    foreach(var old in state.Records.Where(r=>keys.Contains(r.Key)&&!r.Queued).ToList()){token.ThrowIfCancellationRequested();UpdateQueue(old);history.Save(library.Cnpj,library.Environment,state);if(old.Status=="Incerta"){Pause();return 0;}}
    foreach(var item in docs.Where(d=>keys.Contains(d.Key)&&Eligible(d,library.Cnpj,docs)).GroupBy(d=>d.Key).Select(g=>g.First())){
     token.ThrowIfCancellationRequested();if(state.Records.Any(r=>r.Key==item.Key))continue;if(!Authorized(item.Key))continue;
     var previous=ExistingEvent(item.Key,docs);if(previous!=null){state.Records.Add(previous);history.Save(library.Cnpj,library.Environment,state);UpdateQueue(previous);history.Save(library.Cnpj,library.Environment,state);log(previous.Message);continue;}
     var recent=state.Records.Where(r=>r.At>now().AddMinutes(-5)).OrderBy(r=>r.At).ToList();if(recent.Count>=20){state.Next=recent[recent.Count-20].At.AddMinutes(5).AddSeconds(1);break;}
     // O lock do cursor também exclui transmissões concorrentes em outra janela local.
     using(states.Lock(library.Cnpj,library.Environment)){
      if(states.Load(library.Cnpj,library.Environment).AllAfter>now())break;
      var record=new ScienceRecord{Key=item.Key,Status="Incerta",At=now(),Message="Confirmação iniciada; uma interrupção não autoriza o reenvio automático."};
      state.Records.Add(record);history.Save(library.Cnpj,library.Environment,state);
      try{
       var transport=client as SefazClient;if(transport!=null)transport.ConfirmationRequest=b=>Rules.WriteAtomic(Path.Combine(library.Folder,"Manifestacoes",item.Key+"_confirmacao_pedido.xml"),b);
       byte[] reply;try{reply=client.Confirm(library.Cnpj,library.Environment,item.Key,token);}finally{if(transport!=null)transport.ConfirmationRequest=null;}
       record.ResponsePath=Path.Combine(library.Folder,"Manifestacoes",item.Key+"_confirmacao_resposta.xml");Rules.WriteAtomic(record.ResponsePath,reply);
       var parsed=Parse(reply,library.Cnpj,library.Environment,item.Key);record.Status=parsed.Status;record.Code=parsed.Code;record.Message=parsed.Message;record.Protocol=parsed.Protocol;history.Save(library.Cnpj,library.Environment,state);
      }catch(Exception ex){record.Message="Resultado incerto: "+ex.Message;history.Save(library.Cnpj,library.Environment,state);Pause();throw;}
     }
     var result=state.Records.Last();UpdateQueue(result);history.Save(library.Cnpj,library.Environment,state);sent++;
     log("Confirmação "+result.Key+": "+result.Status+" ("+result.Code+"). "+result.Message);
     if(new[]{"108","109","280","281","282","283","284","285","286","473","593","999"}.Contains(result.Code)){Pause();break;}
    }
    history.Save(library.Cnpj,library.Environment,state);return sent;
   }
  }
 }
}
