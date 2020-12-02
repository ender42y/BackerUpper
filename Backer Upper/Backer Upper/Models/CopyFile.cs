using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Backer_Upper.Models
{
    public class CopyFile
    {
        public string Name { get; set; }
        public double Size { get; set; }

        public CopyFile() 
        {
            Name = "";
            Size = 0;
        }
        public CopyFile(string fileName, double size)
        {
            Name = fileName;
            Size = size;
        }
    }
}
