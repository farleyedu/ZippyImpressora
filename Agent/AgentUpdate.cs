using System.Diagnostics;
using System.Security.Cryptography;

namespace ZippyPrint;
internal static class AgentUpdate
{
 internal static async Task Check(CancellationToken ct)
 {
  using var client=Protocol.Client();var manifest=await Protocol.Call<Manifest>(client,HttpMethod.Get,"agente/versao",ct:ct);
  if(manifest?.Disponivel!=true||!Version.TryParse(manifest.Versao,out var remote)||remote<=typeof(AgentUpdate).Assembly.GetName().Version||manifest.Sha256?.Length!=64||manifest.Download!="/api/v2/delivery/impressao/agente/instalador")return;
  var directory=Path.Combine(Program.DataPath,"private","updates");Directory.CreateDirectory(directory);var path=Path.Combine(directory,"ZippyPrintSetup.exe");
  using var download=Protocol.DownloadClient();using var response=await download.GetAsync(manifest.Download,HttpCompletionOption.ResponseHeadersRead,ct);
  if(response.RequestMessage?.RequestUri?.Scheme!="https")throw new InvalidDataException("Atualização só por HTTPS.");response.EnsureSuccessStatusCode();
  if(response.Content.Headers.ContentLength>300_000_000)throw new InvalidDataException("Pacote de atualização muito grande.");
  await using(var output=File.Create(path+".tmp"))
  {
   await using var input=await response.Content.ReadAsStreamAsync(ct);var chunk=new byte[65536];int read;long length=0;
   while((read=await input.ReadAsync(chunk,ct))>0){length+=read;if(length>300_000_000)throw new InvalidDataException("Pacote muito grande.");await output.WriteAsync(chunk.AsMemory(0,read),ct);}
  }
  await using(var input=File.OpenRead(path+".tmp"))if(!Convert.ToHexString(await SHA256.HashDataAsync(input,ct)).Equals(manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Pacote de atualização inválido.");
  File.Move(path+".tmp",path,true);
  Process.Start(new ProcessStartInfo(path,"--update --quiet"){UseShellExecute=false,CreateNoWindow=true});
 }
 private sealed record Manifest(bool Disponivel,string? Versao,long? Tamanho,string? Download,string? Sha256);
}
