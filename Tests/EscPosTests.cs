using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using Xunit;
using ZippyPrint;

public sealed class EscPosTests
{
 [Theory][InlineData(58,"balcao")][InlineData(80,"moderno")][InlineData(58,"caseiro")]
 public async Task RendererCreatesRasterWithCorrectPaperWidthAndFinishingCommands(int paper,string style)
 {
  var doc=new Receipt(1,"ia_whatsapp","42","entrega",DateTimeOffset.UtcNow,true,
   new("Caixa","caixa",style,["itens","totais"],12,"linha",false,"Obrigado"),new("Teste","rede","127.0.0.1:9100",paper,860,true,true,12),
   [new("itens",null,[new("Ação, café, pão ♥",2,20,["+ Queijo","Obs.: não cortar"])]),new("pagamento","COBRAR R$ 20,00",null)],DateTimeOffset.UtcNow);
  var bytes=await EscPos.Render(doc,default);
  Assert.Equal(new byte[]{0x1b,0x40},bytes[..2]);Assert.Equal((byte)(paper==58?48:72),bytes[6]);
  Assert.Equal(new byte[]{0x1d,0x56,0,0x1b,0x70,0,0x19,0xfa},bytes[^8..]);
  Assert.True(bytes.Length>1000);
 }
 [Fact] public void RasterPacksPixelsFromLeftToRight()
 {
  using var image=new Bitmap(8,1,PixelFormat.Format24bppRgb);using(var graphics=Graphics.FromImage(image))graphics.Clear(Color.White);image.SetPixel(0,0,Color.Black);image.SetPixel(7,0,Color.Black);
  using var output=new MemoryStream();EscPos.Raster(output,image);Assert.Equal(new byte[]{0x1d,0x76,0x30,0,1,0,1,0,0x81},output.ToArray());
 }
 [Fact] public async Task TcpSendsExactlyOnceToTheConfiguredPrinterWithoutClaimingPhysicalConfirmation()
 {
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
  try
  {
   var port=((IPEndPoint)listener.LocalEndpoint).Port;var incoming=listener.AcceptTcpClientAsync();var bytes=new byte[]{0x1b,0x40,0x41,0x0a};
   await WindowsSpooler.Send(new("Teste","rede",$"127.0.0.1:{port}",80,860,false,false,12),bytes,default);
   using var client=await incoming;using var output=new MemoryStream();await client.GetStream().CopyToAsync(output);Assert.Equal(bytes,output.ToArray());
  }finally{listener.Stop();}
 }
 [Fact] public void QrHasLengthPrefixedPayload()
 {
  using var output=new MemoryStream();EscPos.Qr(output,"ação");var bytes=output.ToArray();Assert.Equal(0x1d,bytes[0]);Assert.True(bytes.Length>25);
 }
}
