using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.Design;

namespace IcarusServerManager.Handlers
{
    public class ProcessHandler
    {
        public Process process;
        private string curExe;
        private string[] curVariables;
        public int processStatus = 0;
        public Form1 prnt;

        public ProcessHandler (string exe, string[] variables, Form1 parent)
        {
            StartProcess(exe, variables);
            prnt = parent;
        }

        public async void StartProcess(string exe, string[] variables)
        {
            curExe = exe;
            curVariables = variables;

            if(process != null && process.ExitCode == 0)
            {
                process.Close();
            }

            process = new Process();

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

            process.StartInfo.FileName = curExe + " " + curVariables;

            process.Exited += ProcessEnded;

            process.Start();
            processStatus = 1;

            while (processStatus == 1)
            {
                string output = process.StandardOutput.ReadToEnd();
                prnt.WriteToConsole(output, isGameProcessOutput: true);
            }
        }

        public void ProcessEnded(object o, EventArgs e)
        {
            processStatus = 0;
        }

        public void StopProcess()
        {
            process.Close();
            process.Dispose();
            processStatus = 0;
        }
    }
}
