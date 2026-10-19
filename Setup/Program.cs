using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

internal static class Program
{
 [STAThread] private static void Main(string[] args)
 {
  ApplicationConfiguration.Initialize();var quiet=args.Contains("--quiet");
  using var mutex=new Mutex(true,@"Global\ZippyPrintSetup",out var first);if(!first)return;
  try
  {
   if(args.Contains("--update"))Thread.Sleep(3000);
   var root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ZippyPrint"));
   var data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ZippyPrint");
   var privatePath=Path.Combine(data,"private");Directory.CreateDirectory(privatePath);
   var security=new DirectorySecurity();security.SetAccessRuleProtection(true,false);
   foreach(var sid in new[]{WellKnownSidType.LocalSystemSid,WellKnownSidType.BuiltinAdministratorsSid})security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
   new DirectoryInfo(privatePath).SetAccessControl(security);
   Run("sc.exe","stop","ZippyPrint");Thread.Sleep(2000);
   foreach(var process in Process.GetProcessesByName("ZippyPrintAgent"))
   {try{if(process.MainModule?.FileName is string exe&&Path.GetFullPath(exe).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){process.Kill();process.WaitForExit(10000);}}catch(InvalidOperationException){}}
   Directory.CreateDirectory(root);
   using var payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")??throw new InvalidDataException("Pacote ausente.");using var zip=new ZipArchive(payload);
   foreach(var item in zip.Entries)
   {
    var target=Path.GetFullPath(Path.Combine(root,item.FullName));
    if(!target.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Caminho de instalação inválido.");
    if(string.IsNullOrEmpty(item.Name)){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target)!);item.ExtractToFile(target,true);
   }
   var agent=Path.Combine(root,"ZippyPrintAgent.exe");if(!File.Exists(agent))throw new InvalidDataException("Agente ausente do pacote.");
   var query=Run("sc.exe","query","ZippyPrint");
   var service=Run("sc.exe",query==0?"config":"create","ZippyPrint","binPath=","\""+agent+"\"","start=","auto","DisplayName=","Agente Zippy de Impressão");
   if(service!=0)throw new InvalidOperationException("Não foi possível criar o serviço Windows.");
   Run("sc.exe","failure","ZippyPrint","reset=","86400","actions=","restart/5000/restart/10000/restart/30000");
   using(var key=Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))key.SetValue("ZippyPrint","\""+agent+"\" --tray");
   if(Run("sc.exe","start","ZippyPrint")!=0)throw new InvalidOperationException("O serviço foi instalado, mas não iniciou. Confira o Visualizador de Eventos.");
   if(!quiet){Process.Start(new ProcessStartInfo(agent,"--tray"){UseShellExecute=true});MessageBox.Show("Agente instalado. Use o ícone da bandeja para digitar o código mostrado no Zippy.","Agente Zippy");}
  }
  catch(Exception ex){if(!quiet)MessageBox.Show(ex.Message,"Não foi possível instalar",MessageBoxButtons.OK,MessageBoxIcon.Error);Environment.ExitCode=1;}
 }
 private static int Run(string command,params string[] args)
 {
  var info=new ProcessStartInfo(command){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in args)info.ArgumentList.Add(arg);
  using var process=Process.Start(info)!;process.StandardOutput.ReadToEnd();process.StandardError.ReadToEnd();if(!process.WaitForExit(30000)){process.Kill();throw new TimeoutException("O Windows demorou ao configurar o serviço.");}return process.ExitCode;
 }
}
