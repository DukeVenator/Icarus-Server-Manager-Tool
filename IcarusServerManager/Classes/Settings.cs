using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IcarusServerManager.Classes
{
    public class Settings
    {
        public bool IsInit = true;
        public string ServerLocation = Environment.CurrentDirectory + "\\ServerData";

        public void load()
        {
            this.ServerLocation = Properties.Settings.Default.serverLocation;
        }
    }
}
