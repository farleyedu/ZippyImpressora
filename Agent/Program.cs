using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ZippyPrint;
internal static class Program
{
 public static readonly string ServerUrl=typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="ServerUrl").Value!;
 public static readonly string DataPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"ZippyPrint");
 [STAThread] private static void Main(string[] args)
 {
  if(args.Contains("--tray")||args.Contains("--pair"))
  {
   using var single=new Mutex(true,@"Local\ZippyPrintTray",out var first);
   if(!first&&!args.Contains("--pair"))return;
   ApplicationConfiguration.Initialize();
   if(args.Contains("--pair")){Application.Run(new PairWindow());return;}
   using var tray=new TrayContext();Application.Run(tray);return;
  }
  var builder=Host.CreateApplicationBuilder(new HostApplicationBuilderSettings{Args=args,ContentRootPath=AppContext.BaseDirectory});
  builder.Services.AddWindowsService(o=>o.ServiceName="ZippyPrint");
  builder.Services.AddHostedService<PrintService>();
  builder.Build().Run();
 }
}
