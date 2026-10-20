using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;

namespace ZippyPrint;
internal static class Protocol
{
 public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 public const string Api="/api/v2/delivery/impressao/";
 public static string CredentialPath=>Path.Combine(Program.DataPath,"private","station.dat");
 public static void StoreToken(string token)
 {
  Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
  var bytes=ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(token),null,DataProtectionScope.LocalMachine);
  var temp=CredentialPath+".tmp";File.WriteAllBytes(temp,bytes);File.Move(temp,CredentialPath,true);
 }
 public static string? Token()=>File.Exists(CredentialPath)?System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(CredentialPath),null,DataProtectionScope.LocalMachine)):null;
 public static HttpClient Client(string? token=null)
 {
  var url=new Uri(Program.ServerUrl);
  if(url.Scheme!="https")throw new InvalidOperationException("O agente exige HTTPS.");
  var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=url,Timeout=TimeSpan.FromSeconds(20)};
  if(token!=null)client.DefaultRequestHeaders.Add("X-Zippy-Station",token);
  return client;
 }
 /// <summary>Só para baixar o instalador: o servidor redireciona para o GitHub Releases e o arquivo é grande.
 /// Sem o token da estação (ele não pode ir para outro domínio); a integridade vem do SHA-256 do manifesto.</summary>
 public static HttpClient DownloadClient()
 {
  var url=new Uri(Program.ServerUrl);
  if(url.Scheme!="https")throw new InvalidOperationException("O agente exige HTTPS.");
  return new HttpClient(new HttpClientHandler{AllowAutoRedirect=true,MaxAutomaticRedirections=5}){BaseAddress=url,Timeout=TimeSpan.FromMinutes(30)};
 }
 public static async Task<T?> Call<T>(HttpClient client,HttpMethod method,string path,object? body=null,CancellationToken ct=default)
 {
  using var request=new HttpRequestMessage(method,Api+path);
  if(body!=null)request.Content=JsonContent.Create(body,options:Json);
  using var response=await client.SendAsync(request,ct);
  if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)throw new UnauthorizedAccessException("Estação não autorizada. Pareie novamente no Zippy.");
  if(!response.IsSuccessStatusCode)throw new HttpRequestException("Não foi possível falar com o Zippy ("+(int)response.StatusCode+").",null,response.StatusCode);
  var envelope=await response.Content.ReadFromJsonAsync<Envelope<T>>(Json,ct)??throw new InvalidDataException("Resposta vazia do servidor.");
  if(!envelope.Success)throw new InvalidDataException(envelope.Error??"Operação recusada pelo servidor.");
  return envelope.Data;
 }
 private sealed record Envelope<T>(bool Success,T? Data,string? Error);
}
internal sealed record AgentJob(Guid Id,Receipt? Documento);
internal sealed record Receipt(int Versao,string Origem,string Numero,string Modalidade,DateTimeOffset CriadoEm,bool Reimpressao,Template Modelo,Printer Impressora,Block[] Blocos,DateTimeOffset EmitidoEm);
internal sealed record Template(string Nome,string Tipo,string Estilo,string[] Blocos,int TamanhoTexto,string Separador,bool MostrarLogo,string Rodape);
internal sealed record Printer(string Nome,string Conexao,string Endereco,int PapelMm,int PaginaCodigo,bool Corta,bool AbreGaveta,int TamanhoTexto);
internal sealed record Block(string Tipo,string? Texto,Line[]? Itens);
internal sealed record Line(string Nome,decimal Quantidade,decimal? Total,string[] Detalhes);
internal sealed record Claim(bool Recebido);
internal sealed record PendingResult(string Estado,string? Motivo,Guid SessaoId);
