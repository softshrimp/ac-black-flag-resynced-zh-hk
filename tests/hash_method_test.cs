using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
class HashMethodTest {
    sealed class ShortReads:MemoryStream {
        internal ShortReads(byte[] bytes):base(bytes,false){}
        public override int Read(byte[] b,int offset,int count){return base.Read(b,offset,Math.Min(count,7));}
    }
    static void Main(string[] args) {
        var assembly=Assembly.LoadFile(Path.GetFullPath(args[0]));
        var method=assembly.GetType("ACBFLocalPatch.Engine").GetMethod("HashAll",BindingFlags.NonPublic|BindingFlags.Static);
        foreach(int length in new int[]{0,1,63,64,65,4095,4096,4097,65535,65536,65537,8388607,8388608,8388609,16777233}) {
            byte[] bytes=new byte[length];for(int i=0;i<length;i++)bytes[i]=(byte)(i*31+17);
            string expected;using(var h=SHA256.Create())expected=BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            using(var s=new MemoryStream(bytes,false)) {
                if(length>0)s.Position=length/2;
                if((string)method.Invoke(null,new object[]{s})!=expected || s.Position!=length)throw new Exception("Hash mismatch "+length);
            }
            if(length<131072)using(var s=new ShortReads(bytes))if((string)method.Invoke(null,new object[]{s})!=expected)throw new Exception("Short read mismatch");
        }
        Console.WriteLine("15 buffer boundary vectors and 7-byte short-read streams passed against actual installer HashAll.");
    }
}
