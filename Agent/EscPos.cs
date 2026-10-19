using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ZippyPrint;
internal static class EscPos
{
 public static async Task<byte[]> Render(Receipt doc,CancellationToken ct)
 {
  var width=doc.Impressora.PapelMm==58?384:576;
  using var output=new MemoryStream();output.Write([0x1b,0x40]);
  // All user text is rasterized: accents, ornaments and ESC control characters cannot become printer commands.
  var size=Math.Clamp(Math.Max(doc.Modelo.TamanhoTexto,doc.Impressora.TamanhoTexto),11,24)*1.6f;
  using var regular=new Font(doc.Modelo.Estilo=="moderno"?"Arial":"Consolas",size,FontStyle.Regular,GraphicsUnit.Pixel);
  using var bold=new Font(regular,FontStyle.Bold);using var large=new Font(regular.FontFamily,Math.Min(44,size*1.5f),FontStyle.Bold,GraphicsUnit.Pixel);
  void Text(string? text,Font? font=null,bool center=false,bool inverse=false,bool box=false)
  {
   if(string.IsNullOrWhiteSpace(text))return;
   text=text.Replace("\u001b","").Replace("\u001d","");
   if(text.Length>200_000)throw new InvalidDataException("Bloco de cupom muito longo.");
   font??=regular;
   using var sample=new Bitmap(width,1);using var measure=Graphics.FromImage(sample);
   using var format=new StringFormat{Alignment=center?StringAlignment.Center:StringAlignment.Near,Trimming=StringTrimming.None};
   var height=(int)Math.Ceiling(measure.MeasureString(text,font,width-24,format).Height)+20;
   if(height>32000)throw new InvalidDataException("Bloco de cupom muito longo.");
   using var bitmap=new Bitmap(width,height,PixelFormat.Format24bppRgb);using(var g=Graphics.FromImage(bitmap))
   {g.Clear(inverse?Color.Black:Color.White);g.TextRenderingHint=TextRenderingHint.SingleBitPerPixelGridFit;g.DrawString(text,font,inverse?Brushes.White:Brushes.Black,new RectangleF(12,10,width-24,height-20),format);if(box)g.DrawRectangle(Pens.Black,1,1,width-3,height-3);}
   Raster(output,bitmap);
  }
  void Separator()
  {
   var line=doc.Modelo.Separador switch{"duplo"=>new string('=',width/15),"pontilhado"=>new string('-',width/15),"ondas"=>new string('~',width/15),_=>new string('─',width/15)};
   Text(line,center:true);
  }
  var origin=doc.Origem switch{"ifood"=>"IFOOD","cardapio_web"=>"CARDÁPIO WEB","ia_whatsapp"=>"WHATSAPP","atendente"=>"BALCÃO",_=>"PRÓPRIO"};
  var tz=TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
  if(doc.Reimpressao)Text("REIMPRESSÃO "+TimeZoneInfo.ConvertTime(doc.EmitidoEm,tz).ToString("HH:mm"),bold,true,box:true);
  if(doc.Modelo.Estilo=="caseiro")Text("✿ ·· ✿ ·· ✿ ·· ✿",bold,true);
  Text(origin+" · "+doc.Modalidade.ToUpperInvariant(),bold,true,doc.Modelo.Estilo=="moderno");
  Text("Pedido #"+doc.Numero,large,true);Text(TimeZoneInfo.ConvertTime(doc.CriadoEm,tz).ToString("dd/MM HH:mm")+" · "+doc.Modelo.Nome,center:true);
  foreach(var block in doc.Blocos)
  {
   ct.ThrowIfCancellationRequested();
   if(block.Tipo=="logo")
   {
    if(doc.Modelo.MostrarLogo)await Logo(output,block.Texto,width,ct);
   }
   else if(block.Tipo=="qr"&&!string.IsNullOrWhiteSpace(block.Texto))Qr(output,block.Texto);
   else if(block.Tipo=="itens")
   {
    foreach(var item in block.Itens??[])
    {
     Text($"{item.Quantidade} × {item.Nome}"+(doc.Modelo.Blocos.Contains("totais")&&item.Total.HasValue?" · "+item.Total.Value.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")):""),bold);
     foreach(var line in item.Detalhes)Text("  "+line);
    }Separator();
   }
   else
   {
    Text(block.Texto,block.Tipo is "pagamento" or "totais" or "loja"?bold:regular,block.Tipo is "loja" or "rodape" or "fiscal",box:block.Tipo=="pagamento");
    if(block.Tipo is "loja" or "endereco" or "observacoes")Separator();
   }
  }
  if(doc.Modelo.Estilo=="caseiro")Text("Feito com carinho ♥",center:true);
  output.Write([0x1b,0x64,0x04]);if(doc.Impressora.Corta)output.Write([0x1d,0x56,0x00]);if(doc.Impressora.AbreGaveta)output.Write([0x1b,0x70,0x00,0x19,0xfa]);return output.ToArray();
 }
 internal static void Raster(Stream output,Bitmap image)
 {
  var widthBytes=(image.Width+7)/8;
  var rect=new Rectangle(0,0,image.Width,image.Height);var data=image.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
  try
  {
   var raw=new byte[Math.Abs(data.Stride)*image.Height];Marshal.Copy(data.Scan0,raw,0,raw.Length);
   for(var start=0;start<image.Height;start+=240)
   {
    var height=Math.Min(240,image.Height-start);output.Write([0x1d,0x76,0x30,0x00,(byte)(widthBytes&255),(byte)(widthBytes>>8),(byte)(height&255),(byte)(height>>8)]);
    var packed=new byte[widthBytes*height];
    for(var y=0;y<height;y++)for(var x=0;x<image.Width;x++)
    {var i=(start+y)*Math.Abs(data.Stride)+x*3;if(raw[i]*.114+raw[i+1]*.587+raw[i+2]*.299<160)packed[y*widthBytes+x/8]|=(byte)(0x80>>(x%8));}
    output.Write(packed);
   }
  }finally{image.UnlockBits(data);}
 }
 private static async Task Logo(Stream output,string? address,int width,CancellationToken ct)
 {
  if(!Uri.TryCreate(address,UriKind.Absolute,out var uri)||uri.Scheme!="https")return;
  // Logos are optional; never delay or fail an order because its CDN is unavailable.
  try
  {
   var addresses=await System.Net.Dns.GetHostAddressesAsync(uri.Host,ct);
   if(addresses.Length==0||addresses.Any(a=>{var b=a.GetAddressBytes();return System.Net.IPAddress.IsLoopback(a)||a.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||b[0] is 0 or 10 or 127||b[0]==169&&b[1]==254||b[0]==172&&b[1]>=16&&b[1]<=31||b[0]==192&&b[1]==168;}))return;
   using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(5)};
   using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);
   if(!response.IsSuccessStatusCode||response.Content.Headers.ContentLength>2_000_000)return;
   await using var stream=await response.Content.ReadAsStreamAsync(ct);using var buffer=new MemoryStream();var chunk=new byte[8192];int read;
   while((read=await stream.ReadAsync(chunk,ct))>0){if(buffer.Length+read>2_000_000)return;buffer.Write(chunk,0,read);}buffer.Position=0;
   using var source=Image.FromStream(buffer);var targetWidth=Math.Min(width/2,source.Width);var targetHeight=Math.Clamp((int)(source.Height*(double)targetWidth/source.Width),1,200);
   using var bitmap=new Bitmap(width,targetHeight,PixelFormat.Format24bppRgb);using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.White);g.DrawImage(source,(width-targetWidth)/2,0,targetWidth,targetHeight);}Raster(output,bitmap);
  }catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or ArgumentException or IOException){ }
 }
 internal static void Qr(Stream output,string text)
 {
  var bytes=Encoding.UTF8.GetBytes(text);if(bytes.Length>2000)throw new InvalidDataException("QR muito longo.");
  output.Write([0x1d,0x28,0x6b,0x04,0x00,0x31,0x41,0x32,0x00]);output.Write([0x1d,0x28,0x6b,0x03,0x00,0x31,0x43,0x06]);
  var size=bytes.Length+3;output.Write([0x1d,0x28,0x6b,(byte)(size&255),(byte)(size>>8),0x31,0x50,0x30]);output.Write(bytes);output.Write([0x1d,0x28,0x6b,0x03,0x00,0x31,0x51,0x30]);
 }
}
