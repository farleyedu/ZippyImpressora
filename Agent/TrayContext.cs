using System.Diagnostics;
using System.Text.Json;

namespace ZippyPrint;
internal sealed class TrayContext:ApplicationContext
{
 private readonly NotifyIcon tray;
 private readonly System.Windows.Forms.Timer timer=new(){Interval=5000};
 public TrayContext()
 {
  var menu=new ContextMenuStrip();menu.Items.Add("Parear estação",null,(_,_)=>Pair());menu.Items.Add("Abrir Zippy",null,(_,_)=>Process.Start(new ProcessStartInfo(Program.ServerUrl){UseShellExecute=true}));menu.Items.Add("Fechar ícone (serviço continua)",null,(_,_)=>ExitThread());
  tray=new NotifyIcon{Icon=SystemIcons.Application,Text="Agente Zippy · iniciando",Visible=true,ContextMenuStrip=menu};
  tray.DoubleClick+=(_,_)=>Pair();timer.Tick+=(_,_)=>Update();timer.Start();Update();
 }
 private static void Pair()
 {try{Process.Start(new ProcessStartInfo(Environment.ProcessPath!,"--pair"){UseShellExecute=true,Verb="runas"});}catch(System.ComponentModel.Win32Exception){}}
 private void Update()
 {
  try
  {
   using var status=JsonDocument.Parse(File.ReadAllText(Path.Combine(Program.DataPath,"status.json")));var root=status.RootElement;
   var fresh=root.GetProperty("at").GetDateTimeOffset()>DateTimeOffset.UtcNow.AddSeconds(-20);
   var online=fresh&&root.GetProperty("online").GetBoolean();var message=fresh?root.GetProperty("message").GetString():"Serviço sem sinal";
   tray.Icon=online?SystemIcons.Information:SystemIcons.Warning;tray.Text=("Zippy · "+message)[..Math.Min(63,("Zippy · "+message).Length)];
  }catch(Exception ex)when(ex is IOException or JsonException){tray.Text="Zippy · aguardando serviço";}
 }
 protected override void Dispose(bool disposing){if(disposing){timer.Dispose();tray.Visible=false;tray.Dispose();}base.Dispose(disposing);}
}
internal sealed class PairWindow:Form
{
 private readonly TextBox code=new(){MaxLength=6,Width=200,PlaceholderText="Código de 6 dígitos"};
 private readonly Label message=new(){AutoSize=true,MaximumSize=new Size(320,0)};
 private readonly Button pair=new(){Text="Conectar estação",AutoSize=true};
 public PairWindow()
 {
  Text="Agente Zippy · Pareamento";Width=380;Height=240;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(18),WrapContents=false};
  panel.Controls.Add(new Label{Text="No Zippy, abra Impressão → Adicionar estação → Agente.",AutoSize=true,MaximumSize=new Size(320,0)});panel.Controls.Add(code);panel.Controls.Add(pair);panel.Controls.Add(message);Controls.Add(panel);
  pair.Click+=async(_,_)=>await Pair();AcceptButton=pair;
 }
 private async Task Pair()
 {
  if(code.Text.Length!=6||!code.Text.All(char.IsAsciiDigit)){message.Text="Digite os 6 dígitos mostrados no Zippy.";return;}
  pair.Enabled=false;
  try{using var client=Protocol.Client();var result=await Protocol.Call<JsonElement>(client,HttpMethod.Post,"agente/parear",new{codigo=code.Text,computador=Environment.MachineName});Protocol.StoreToken(result.GetProperty("token").GetString()!);message.Text="Estação conectada. Volte ao Zippy para escolher a impressora.";code.Clear();}
  catch(Exception ex)when(ex is HttpRequestException or InvalidDataException or UnauthorizedAccessException or IOException){message.Text=ex is UnauthorizedAccessException?"Código inválido ou expirado. Gere outro no Zippy.":"Não foi possível parear. Confira a conexão e o código.";}
  finally{pair.Enabled=true;}
 }
}
