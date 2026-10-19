using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ZippyPrint;
internal sealed class PrintService(ILogger<PrintService> logger):BackgroundService
{
 private readonly Guid session=Guid.NewGuid();
 private readonly string journalPath=Path.Combine(Program.DataPath,"private","journal.json");
 private Dictionary<Guid,JournalEntry> journal=[];
 private void Persist()
 {
  var tmp=journalPath+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(journal,Protocol.Json));File.Move(tmp,journalPath,true);
 }
 protected override async Task ExecuteAsync(CancellationToken ct)
 {
  Directory.CreateDirectory(Path.Combine(Program.DataPath,"private"));
  if(File.Exists(journalPath))
  {
   try{journal=JsonSerializer.Deserialize<Dictionary<Guid,JournalEntry>>(File.ReadAllText(journalPath),Protocol.Json)??[];}
   catch(Exception ex){logger.LogCritical(ex,"Diário de impressão inválido. Impressão suspensa para evitar repetição.");Status("Diário inválido. Peça suporte.",false);return;}
  }
  foreach(var pair in journal.ToArray())
   if(pair.Value.Result==null)journal[pair.Key]=pair.Value with{Result=new("falhou","O agente reiniciou durante o envio. Confira o papel antes de tentar novamente.",pair.Value.Session)};
  Persist();
  DateTimeOffset nextUpdate=DateTimeOffset.UtcNow,nextProbe=DateTimeOffset.UtcNow;DetectedPrinter[] network=[];
  while(!ct.IsCancellationRequested)
  {
   try
   {
    var token=Protocol.Token();
    if(token==null){Status("Aguardando pareamento",false);await Task.Delay(3000,ct);continue;}
    using var client=Protocol.Client(token);
    if(DateTimeOffset.UtcNow>=nextProbe)
    {
     var configured=await Protocol.Call<Printer[]>(client,HttpMethod.Get,"agente/impressoras",ct:ct)??[];
     using var limit=new SemaphoreSlim(5);
     network=await Task.WhenAll(configured.Where(p=>p.Conexao=="rede").Select(async p=>{await limit.WaitAsync(ct);try{return await WindowsSpooler.Probe(p,ct);}finally{limit.Release();}}));
     nextProbe=DateTimeOffset.UtcNow.AddSeconds(30);
    }
    var localIp=System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==System.Net.NetworkInformation.OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).FirstOrDefault(a=>a.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork&&!System.Net.IPAddress.IsLoopback(a))?.ToString();
    await Protocol.Call<JsonElement>(client,HttpMethod.Post,"agente/sinal",new{sessaoId=session,computador=Environment.MachineName,ipLocal=localIp,versao=typeof(PrintService).Assembly.GetName().Version?.ToString(3),impressoras=WindowsSpooler.List().Concat(network).ToArray()},ct);
    Status("Conectado",true);
    foreach(var pair in journal.Where(p=>!p.Value.Acknowledged).ToArray())
    {
     try{await Protocol.Call<bool>(client,HttpMethod.Post,$"agente/jobs/{pair.Key}/resultado",pair.Value.Result,ct);}
     catch(HttpRequestException error)when(error.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
     {logger.LogWarning("Confirmação antiga do trabalho {Id} não pertence mais à estação. Não será reenviada à impressora.",pair.Key);}
     journal[pair.Key]=pair.Value with{Acknowledged=true};Persist();
    }
    var pending=await Protocol.Call<AgentJob[]>(client,HttpMethod.Get,"agente/jobs/pendentes",ct:ct)??[];
    foreach(var job in pending)
    {
     if(journal.ContainsKey(job.Id))continue;
     var claim=await Protocol.Call<Claim>(client,HttpMethod.Post,$"agente/jobs/{job.Id}/receber?sessaoId={session}",ct:ct);
     if(claim?.Recebido!=true)continue;
     // Journal is durable BEFORE touching hardware. A crash can never trigger an automatic resend.
     journal[job.Id]=new(session,null,false,DateTimeOffset.UtcNow);Persist();
     PendingResult result;
     try
     {
      if(job.Documento==null||job.Documento.Versao!=1)throw new InvalidDataException("Formato de cupom não suportado. Atualize o agente.");
      var bytes=await EscPos.Render(job.Documento,ct);
      await WindowsSpooler.Send(job.Documento.Impressora,bytes,ct);
      result=new("enviado",null,session);
     }
     catch(Exception ex) when(ex is not OperationCanceledException||!ct.IsCancellationRequested)
     {logger.LogWarning("Falha ao enviar trabalho {JobId}: {Type}",job.Id,ex.GetType().Name);result=new("falhou",Friendly(ex),session);}
     journal[job.Id]=journal[job.Id] with{Result=result};Persist();
     await Protocol.Call<bool>(client,HttpMethod.Post,$"agente/jobs/{job.Id}/resultado",result,ct);
     journal[job.Id]=journal[job.Id] with{Acknowledged=true};Persist();
    }
    // Keep enough durable IDs for recovery; old terminal jobs cannot be reclaimed on the server.
    foreach(var id in journal.Where(p=>p.Value.Acknowledged&&p.Value.CreatedAt<DateTimeOffset.UtcNow.AddDays(-30)).Select(p=>p.Key).ToArray())journal.Remove(id);
    if(DateTimeOffset.UtcNow>=nextUpdate){nextUpdate=DateTimeOffset.UtcNow.AddHours(1);await AgentUpdate.Check(ct);}
   }
   catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
   catch(UnauthorizedAccessException){Status("Estação revogada ou desativada. Confira no Zippy.",false);}
   catch(Exception ex){logger.LogWarning("Agente desconectado: {Type}",ex.GetType().Name);Status("Sem conexão com o Zippy. Tentando novamente…",false);}
   try{await Task.Delay(3000,ct);}catch(OperationCanceledException){break;}
  }
 }
 private static string Friendly(Exception ex)=>ex switch
 {PrinterStateException=>ex.Message,System.Net.Sockets.SocketException=>"Não consegui falar com a impressora de rede. Confira IP, cabo e energia.",TimeoutException=>"A impressora não respondeu a tempo.",System.ComponentModel.Win32Exception=>"O Windows não conseguiu enviar à impressora. Confira a fila de impressão.",_=>"Não foi possível enviar o cupom. Confira conexão, papel e tampa antes de tentar de novo."};
 private static void Status(string message,bool online)
 {try{var path=Path.Combine(Program.DataPath,"status.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new{message,online,at=DateTimeOffset.UtcNow},Protocol.Json));File.Move(path+".tmp",path,true);}catch(IOException){}}
 private sealed record JournalEntry(Guid Session,PendingResult? Result,bool Acknowledged,DateTimeOffset CreatedAt);
}
