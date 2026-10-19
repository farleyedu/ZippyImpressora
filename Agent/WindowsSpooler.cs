using System.ComponentModel;
using System.Drawing.Printing;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ZippyPrint;
internal static class WindowsSpooler
{
 public static DetectedPrinter[] List()=>System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>().Select(name=>new DetectedPrinter(name,State(name),"sistema",name)).ToArray();
 private static string State(string name)
 {
  if(!OpenPrinter(name,out var handle,IntPtr.Zero))return "offline";
  try
  {
   var buffer=Marshal.AllocHGlobal(4);
   try
   {
    if(!GetPrinter(handle,6,buffer,4,out _))return "desconhecido";var flags=(uint)Marshal.ReadInt32(buffer);
    return (flags&0x10)!=0?"sem_papel":(flags&0x400000)!=0?"tampa_aberta":(flags&0x80)!=0?"offline":(flags&0x2)!=0?"erro":"pronta";
   }finally{Marshal.FreeHGlobal(buffer);}
  }finally{ClosePrinter(handle);}
 }
 public static async Task<DetectedPrinter> Probe(Printer printer,CancellationToken ct)
 {
  var state="desconhecido";
  if(printer.Conexao=="rede")
  {
   try{var parts=printer.Endereco.Split(':');using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(1000);using var tcp=new TcpClient();await tcp.ConnectAsync(parts[0],int.Parse(parts[1]),timeout.Token);state="pronta";}
   catch(Exception ex)when(ex is SocketException or OperationCanceledException or FormatException or IndexOutOfRangeException){state="offline";}
  }
  else state=State(printer.Endereco);
  return new(printer.Nome,state,printer.Conexao,printer.Endereco);
 }
 public static async Task Send(Printer printer,byte[] bytes,CancellationToken ct)
 {
  if(printer.Conexao=="rede")
  {
   var parts=printer.Endereco.Split(':');
   if(parts.Length!=2||!System.Net.IPAddress.TryParse(parts[0],out var ip)||!int.TryParse(parts[1],out var port)||port is <1 or >65535)throw new InvalidDataException("IP da impressora inválido.");
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(10000);
   using var tcp=new TcpClient();await tcp.ConnectAsync(ip,port,timeout.Token);await tcp.GetStream().WriteAsync(bytes,timeout.Token);await tcp.GetStream().FlushAsync(timeout.Token);return;
  }
  if(printer.Conexao!="sistema")throw new InvalidDataException("Conexão de impressora não suportada.");
  var state=State(printer.Endereco);
  if(state=="sem_papel")throw new PrinterStateException("A impressora está sem papel, segundo o Windows.");
  if(state=="tampa_aberta")throw new PrinterStateException("A tampa da impressora está aberta, segundo o Windows.");
  if(state=="offline")throw new PrinterStateException("A impressora está offline no Windows. Confira cabo e energia.");
  // The Windows service must be able to see the machine-wide queue (not a per-user redirected queue).
  if(!OpenPrinter(printer.Endereco,out var handle,IntPtr.Zero))throw new Win32Exception();
  try
  {
   var info=new DocInfo{DocumentName="Zippy · cupom",DataType="RAW"};
   if(StartDocPrinter(handle,1,ref info)==0)throw new Win32Exception();
   try
   {
    if(!StartPagePrinter(handle))throw new Win32Exception();
    try{if(!WritePrinter(handle,bytes,bytes.Length,out var written)||written!=bytes.Length)throw new Win32Exception();}
    finally{EndPagePrinter(handle);}
   }finally{EndDocPrinter(handle);}
  }finally{ClosePrinter(handle);}
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct DocInfo{[MarshalAs(UnmanagedType.LPWStr)]public string DocumentName;[MarshalAs(UnmanagedType.LPWStr)]public string? OutputFile;[MarshalAs(UnmanagedType.LPWStr)]public string DataType;}
 [DllImport("winspool.drv",EntryPoint="OpenPrinterW",SetLastError=true,CharSet=CharSet.Unicode)]private static extern bool OpenPrinter(string name,out IntPtr handle,IntPtr defaults);
 [DllImport("winspool.drv",EntryPoint="StartDocPrinterW",SetLastError=true,CharSet=CharSet.Unicode)]private static extern int StartDocPrinter(IntPtr handle,int level,ref DocInfo info);
 [DllImport("winspool.drv",SetLastError=true)]private static extern bool StartPagePrinter(IntPtr handle);
 [DllImport("winspool.drv",SetLastError=true)]private static extern bool WritePrinter(IntPtr handle,byte[] data,int count,out int written);
 [DllImport("winspool.drv")]private static extern bool EndPagePrinter(IntPtr handle);
 [DllImport("winspool.drv")]private static extern bool EndDocPrinter(IntPtr handle);
 [DllImport("winspool.drv")]private static extern bool ClosePrinter(IntPtr handle);
 [DllImport("winspool.drv",EntryPoint="GetPrinterW",SetLastError=true)]private static extern bool GetPrinter(IntPtr handle,int level,IntPtr buffer,int length,out int needed);
}
internal sealed record DetectedPrinter(string Nome,string Estado,string Conexao,string Endereco);
internal sealed class PrinterStateException(string message):Exception(message);
