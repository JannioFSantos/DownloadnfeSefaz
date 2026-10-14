using System;
using System.IO;
using System.Windows.Forms;
using System.Reflection;
[assembly:AssemblyTitle("XML A1")]
[assembly:AssemblyDescription("Consulta NF-e com A1 e biblioteca local de XMLs")]
[assembly:AssemblyVersion("1.4.0.0")]
namespace XmlA1 {
 static class Program {
  [STAThread] static int Main(string[] args) {
   if(Array.IndexOf(args,"--self-test")>=0)return SelfTests.Run(args);
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Application.ThreadException+=delegate(object s,System.Threading.ThreadExceptionEventArgs e){MessageBox.Show(e.Exception.Message,"XML A1",MessageBoxButtons.OK,MessageBoxIcon.Error);};
   Application.Run(new MainForm(Array.IndexOf(args,"--demo")>=0));return 0;
  }
 }
}
