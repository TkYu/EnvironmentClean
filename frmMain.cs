using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EnvironmentClean
{
    public partial class frmMain : Form
    {

        [DllImport("user32")]
        public static extern UInt32 SendMessage(IntPtr hWnd, UInt32 msg, UInt32 wParam, UInt32 lParam);

        internal const int BCM_FIRST = 0x1600;
        internal const int BCM_SETSHIELD = (BCM_FIRST + 0x000C);

        private bool IsAdmin => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        private Dictionary<string, string> userEnvironmentVariable
        {
            get
            {
                var reg = Registry.CurrentUser.OpenSubKey("Environment", false);
                var ret = new Dictionary<string,string>();
                if (reg == null) return ret;
                foreach (var subKeyName in reg.GetValueNames()) ret.Add(subKeyName,reg.GetValue(subKeyName,null,RegistryValueOptions.DoNotExpandEnvironmentNames).ToString());
                return ret;
            }
        }

        private Dictionary<string, string> machineEnvironmentVariable
        {
            get
            {
                var reg = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", false);
                var ret = new Dictionary<string,string>();
                if (reg == null) return ret;
                foreach (var subKeyName in reg.GetValueNames()) ret.Add(subKeyName,reg.GetValue(subKeyName,null,RegistryValueOptions.DoNotExpandEnvironmentNames).ToString());
                return ret;
            }
        }

        public frmMain()
        {
            InitializeComponent();
            Init();
            if (Environment.CommandLine.Contains("doaction"))
            {
                var index = Environment.CommandLine.IndexOf("doaction", StringComparison.Ordinal);
                var name = Environment.CommandLine.Remove(0,index+8);
                if (tableLayoutPanel2.Controls.ContainsKey(name))
                {
                    var btn = (Button) tableLayoutPanel2.Controls[name];
                    Task.Run(async () =>
                    {
                        await Task.Delay(300);
                        btn.PerformClick();
                    });
                }
            }
            if (!IsAdmin)
            {
                AddShieldToButton(btnClearInvalidPath);
                AddShieldToButton(btnClearEmpty);
                AddShieldToButton(btnClearDupKeys);
                AddShieldToButton(btnUpgradePath);
            }
        }

        private void AppendText(string text, Color color, bool addNewLine = false)
        {
            richTextBox1.SuspendLayout();
            richTextBox1.SelectionColor = color;
            richTextBox1.AppendText(addNewLine
                ? $"{text};{Environment.NewLine}"
                : text);
            richTextBox1.ScrollToCaret();
            richTextBox1.ResumeLayout();
        }

        public delegate void InvokeDelegate();
        private void Init()
        {
            if (InvokeRequired)
            {
                Invoke(new InvokeDelegate(Init));
            }
            else
            {
                var nodeUser = treeView1.Nodes["nodeUser"];
                var nodeMachine = treeView1.Nodes["nodeMachine"];

                nodeUser.Nodes.Clear();
                nodeMachine.Nodes.Clear();

                var ue = userEnvironmentVariable;
                var me = machineEnvironmentVariable;

                foreach (string environmentVariable in ue.Keys.OrderBy(c => c))
                {
                    AppendChild(nodeUser, "node_user_", environmentVariable, ue[environmentVariable]);
                }

                foreach (string environmentVariable in me.Keys.OrderBy(c => c))
                {
                    AppendChild(nodeMachine, "node_machine_", environmentVariable, me[environmentVariable]);
                }

                foreach (var key in me.Keys)
                {
                    var upperKey = key.ToUpper();
                    if (upperKey == "TEMP" || upperKey == "TMP" || upperKey == "PATH" || upperKey == "PATHEXT")
                        continue;
                    if (userEnvironmentVariable.ContainsKey(key))
                    {
                        nodeUser.Nodes["node_user_" + key].ForeColor = Color.Blue;
                        nodeMachine.Nodes["node_machine_" + key].ForeColor = Color.Blue;
                    }
                }

                var groupsUser = ue.Where(ev => ev.Key.ToUpper() != "TEMP").GroupBy(ev => ev.Value).Where(g => g.Count() > 1);
                foreach (var group in groupsUser)
                foreach (var keyValuePair in @group.ToArray()) nodeUser.Nodes["node_user_" + keyValuePair.Key].ForeColor = Color.Cyan;

                var groupsMachine = me.Where(ev => ev.Key.ToUpper() != "TEMP").GroupBy(ev => ev.Value).Where(g => g.Count() > 1);
                foreach (var group in groupsMachine)
                foreach (var keyValuePair in @group.ToArray()) nodeMachine.Nodes["node_machine_" + keyValuePair.Key].ForeColor = Color.Cyan;

                void AppendChild(TreeNode parent, string prefix, string key,string value)
                {
                    var spl = value.Split(';');
                    if (spl.Length == 1 && spl[0].Length > 3 && !string.IsNullOrEmpty(spl[0]) && spl[0][1] == ':' && spl[0].Contains("\\") && !spl[0].Contains("System32") && !Directory.Exists(spl[0]) && !File.Exists(spl[0]))
                        parent.Nodes.Add(new TreeNode {Name = prefix + key, Text = key, ForeColor = Color.Red});
                    else if (string.IsNullOrWhiteSpace(value))
                        parent.Nodes.Add(new TreeNode {Name = prefix + key, Text = key, ForeColor = Color.DarkOrchid});
                    else
                        parent.Nodes.Add(prefix + key, key);
                }

                treeView1.ExpandAll();
            }
        }



        private static void AddShieldToButton(Button b)
        {
            b.FlatStyle = FlatStyle.System;
            SendMessage(b.Handle, BCM_SETSHIELD, 0, 0xFFFFFFFF);
        }

        private static void RestartElevated(string args)
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = Application.ExecutablePath,
                Arguments = args,
                Verb = "runas"
            };
            try
            {
                var p = Process.Start(startInfo);
            }
            catch
            {
                return; //If cancelled, do nothing
            }

            Application.Exit();
        }

        private async void btnClearInvalidPath_Click(object sender, EventArgs e)
        {
            var btn = (Button) sender;
            var txt = btn.Text;
            btn.Text = "Please Wait...";
            btn.Enabled = false;
            if (IsAdmin)
            {
                var lst = new Dictionary<string,string>();

                await Task.Run(() =>
                {
                    foreach (string environmentVariable in userEnvironmentVariable.Keys)
                    {
                        var spl = userEnvironmentVariable[environmentVariable].Split(';');
                        if (spl.Length == 1 && spl[0].Length > 3 && !string.IsNullOrEmpty(spl[0]) && spl[0][1] == ':' && spl[0].Contains("\\") && !spl[0].Contains("System32") && !Directory.Exists(spl[0]) && !File.Exists(spl[0]))
                        {
                            lst.Add(environmentVariable, userEnvironmentVariable[environmentVariable]);
                            Environment.SetEnvironmentVariable(environmentVariable, null, EnvironmentVariableTarget.User);
                        }
                    }


                    foreach (string environmentVariable in machineEnvironmentVariable.Keys)
                    {
                        var spl = machineEnvironmentVariable[environmentVariable].Split(';');
                        if (spl.Length == 1 && spl[0].Length > 3 && !string.IsNullOrEmpty(spl[0]) && spl[0][1] == ':' && spl[0].Contains("\\") && !spl[0].Contains("System32") && !Directory.Exists(spl[0]) && !File.Exists(spl[0]))
                        {
                            lst.Add(environmentVariable, machineEnvironmentVariable[environmentVariable]);
                            Environment.SetEnvironmentVariable(environmentVariable, null, EnvironmentVariableTarget.Machine);
                        }
                    }

                    if (lst.Any())
                    {
                        File.WriteAllText($"Backup{DateTime.Now:yyMMddHHmmss}.txt", string.Join("\n", lst.Select(c => $"{c.Key}={c.Value}")));
                        MessageBox.Show($"Invalid Keys ({string.Join(",", lst.Keys)}) Removed");
                    }
                });

                btn.Text = txt;
                btn.Enabled = true;
                Init();
            }
            else
            {
                RestartElevated("doaction" + btn.Name);
            }
        }

        private async void btnClearEmpty_Click(object sender, EventArgs e)
        {
            var btn = (Button) sender;
            var txt = btn.Text;
            btn.Text = "Please Wait...";
            btn.Enabled = false;
            if (IsAdmin)
            {
                var lst = new Dictionary<string,string>();

                await Task.Run(() =>
                {
                    foreach (string environmentVariable in userEnvironmentVariable.Keys)
                    {
                        if (string.IsNullOrWhiteSpace(userEnvironmentVariable[environmentVariable]))
                        {
                            lst.Add(environmentVariable, "Empty");
                            Environment.SetEnvironmentVariable(environmentVariable, null, EnvironmentVariableTarget.User);
                        }
                    }


                    foreach (string environmentVariable in machineEnvironmentVariable.Keys)
                    {
                        if (string.IsNullOrWhiteSpace(machineEnvironmentVariable[environmentVariable]))
                        {
                            lst.Add(environmentVariable, "Empty");
                            Environment.SetEnvironmentVariable(environmentVariable, null, EnvironmentVariableTarget.Machine);
                        }
                    }

                    if (lst.Any())
                    {
                        File.WriteAllText($"Backup{DateTime.Now:yyMMddHHmmss}.txt", string.Join("\n", lst.Select(c => $"{c.Key}={c.Value}")));
                        MessageBox.Show($"Empty Value Keys ({string.Join(",", lst.Keys)}) Removed");
                    }
                });

                btn.Text = txt;
                btn.Enabled = true;
                Init();
            }
            else
            {
                RestartElevated("doaction" + btn.Name);
            }
        }

        private async void btnClearDupKeys_Click(object sender, EventArgs e)
        {
            var btn = (Button) sender;
            var txt = btn.Text;
            btn.Text = "Please Wait...";
            btn.Enabled = false;
            if (IsAdmin)
            {
                var lst = new Dictionary<string,string>();

                await Task.Run(() =>
                {
                    foreach (var key in machineEnvironmentVariable.Keys)
                    {
                        var upperKey = key.ToUpper();
                        if (upperKey == "TEMP" || upperKey == "TMP" || upperKey == "PATH" || upperKey == "PATHEXT")
                            continue;
                        if (userEnvironmentVariable.ContainsKey(key) && userEnvironmentVariable[key] == machineEnvironmentVariable[key])
                        {
                            lst.Add(key,userEnvironmentVariable[key]);
                            Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.User);
                        }
                    }

                    if (lst.Any())
                    {
                        File.WriteAllText($"Backup{DateTime.Now:yyMMddHHmmss}.txt", string.Join("\n", lst.Select(c => $"{c.Key}={c.Value}")));
                        MessageBox.Show($"Duplicate User Keys ({string.Join(",", lst.Keys)}) Removed");
                    }
                });

                btn.Text = txt;
                btn.Enabled = true;
                Init();
            }
            else
            {
                RestartElevated("doaction" + btn.Name);
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var okdirs = userEnvironmentVariable.Where(c => !string.IsNullOrEmpty(c.Value) && c.Value.Length > 3 && c.Value[1] == ':' && c.Value.Contains("\\") && Directory.Exists(c.Value)).ToList();
            okdirs.AddRange(machineEnvironmentVariable.Where(c => !string.IsNullOrEmpty(c.Value) && c.Value.Length > 3 && c.Value[1] == ':' && c.Value.Contains("\\") && Directory.Exists(c.Value)));
            if (e.Node.Name.StartsWith("node_user_"))
            {
                richTextBox1.Text = "";
                var key = e.Node.Name.Remove(0, 10);
                var spl = userEnvironmentVariable[key].Split(';');
                PrintToRtBox(spl);
            }
            else if (e.Node.Name.StartsWith("node_machine_"))
            {
                richTextBox1.Text = "";
                var key = e.Node.Name.Remove(0, 13);
                var spl = machineEnvironmentVariable[key].Split(';');
                PrintToRtBox(spl);
            }

            void PrintToRtBox(string[] spl)
            {
                foreach (var s in spl)
                {
                    if(string.IsNullOrWhiteSpace(s))continue;
                    if (s.Length > 3 && s[1] == ':' && s.Contains("\\"))
                    {
                        if (Directory.Exists(s) || File.Exists(s))
                        {
                            if (okdirs.Any(c => s.Contains(c.Value) && s != c.Value))
                            {
                                var item = okdirs.First(c => s.Contains(c.Value) && s != c.Value);
                                AppendText($"{s} => {s.Replace(item.Value, $"%{item.Key}%")}", Color.Blue, true);
                            }
                            else
                            {
                                AppendText(s, Color.Green,true);
                            }
                        }
                        else
                        {
                            AppendText(s, s.Contains("System32") ? Color.DarkSlateBlue : Color.Red, true);
                        }
                    }
                    else
                    {
                        var expand = Environment.ExpandEnvironmentVariables(s);
                        if (expand.Length > 3 && expand[1] == ':' && expand.Contains("\\"))
                        {
                            if (Directory.Exists(expand) || File.Exists(expand))
                                AppendText(s, Color.Green,true);
                            else if(expand.Contains("System32"))
                                AppendText(s, Color.DarkSlateBlue,true);
                            else
                                AppendText(s, Color.Red,true);
                        }
                        else
                        {

                            AppendText(s, Color.Black, true);
                        }
                    }
                }
            }
        }

        private async void btnUpgradePath_Click(object sender, EventArgs e)
        {
            var btn = (Button) sender;
            var txt = btn.Text;
            btn.Text = "Please Wait...";
            btn.Enabled = false;
            if (IsAdmin)
            {
                string user = null, machine = null;
                await Task.Run(() =>
                {
                    var okdirs = userEnvironmentVariable.Where(c => !string.IsNullOrEmpty(c.Value) && c.Value.Length > 3 && c.Value[1] == ':' && c.Value.Contains("\\") && Directory.Exists(c.Value)).ToList();
                    okdirs.AddRange(machineEnvironmentVariable.Where(c => !string.IsNullOrEmpty(c.Value) && c.Value.Length > 3 && c.Value[1] == ':' && c.Value.Contains("\\") && Directory.Exists(c.Value)));

                    var ue = Registry.CurrentUser.OpenSubKey("Environment", true);
                    var me = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment", true);
                    if (ue != null && me != null)
                    {
                        var up = ue.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames).ToString();
                        var uProcess = Process(up.Split(';'));
                        if (up != uProcess)
                        {
                            user = up;
                            ue.SetValue("Path",uProcess,RegistryValueKind.String);
                        }

                        var mp = me.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames).ToString();
                        var mProcess = Process(mp.Split(';'));
                        if (mp != mProcess)
                        {
                            machine = mp;
                            me.SetValue("Path",mProcess,RegistryValueKind.String);
                        }

                    }
                    if(!string.IsNullOrEmpty(user))File.WriteAllText($"BackupUserPath{DateTime.Now:yyMMddHHmmss}.txt", user);
                    if(!string.IsNullOrEmpty(machine))File.WriteAllText($"BackupMachinePath{DateTime.Now:yyMMddHHmmss}.txt", machine);
                    if (!string.IsNullOrEmpty(user) || !string.IsNullOrEmpty(machine))
                    {
                        MessageBox.Show("Path cleaned!");
                    }


                    string Process(string[] spl)
                    {
                        var res = new List<string>();
                        foreach (var s in spl)
                        {
                            if (string.IsNullOrWhiteSpace(s)) continue;
                            if (s.Length > 3 && s[1] == ':' && s.Contains("\\"))
                            {
                                if (Directory.Exists(s) || File.Exists(s))
                                {
                                    if (okdirs.Any(c => s.Contains(c.Value) && s != c.Value))
                                    {
                                        var item = okdirs.First(c => s.Contains(c.Value) && s != c.Value);
                                        res.Add(s.Replace(item.Value, $"%{item.Key}%"));
                                    }
                                    else
                                    {
                                        res.Add(s);
                                    }
                                }
                                else
                                {
                                    if(s.Contains("System32"))
                                        res.Add(s);
                                }
                            }
                            else
                            {
                                var expand = Environment.ExpandEnvironmentVariables(s);
                                if (expand.Length > 3 && expand[1] == ':' && expand.Contains("\\"))
                                {
                                    if (Directory.Exists(expand) || File.Exists(expand))
                                        res.Add(s);
                                    else if (expand.Contains("System32"))
                                        res.Add(s);
                                }
                                else
                                {

                                    res.Add(s);
                                }
                            }
                        }
                        return string.Join(";", res);
                    }
                });
                btn.Text = txt;
                btn.Enabled = true;

            }
            else
            {
                RestartElevated("doaction" + btn.Name);
            }
        }

        private async void delToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if(treeView1.SelectedNode==null)return;
            if (!treeView1.SelectedNode.Name.StartsWith("node_")) return;
            if (MessageBox.Show($"Confirm to delete {treeView1.SelectedNode.Text}?", "Question", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Enabled = false;
                var txt = Text;
                Text = "Please Wait...";
                if (treeView1.SelectedNode.Name.StartsWith("node_user_"))
                {
                    var key = treeView1.SelectedNode.Name.Remove(0, 10);
                    await Task.Run(() =>
                    {
                        File.WriteAllText($"Backup{DateTime.Now:yyMMddHHmmss}.txt", $"{key}={userEnvironmentVariable[key]}");
                        Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.User);
                    });
                    Enabled = true;
                    Text = txt;
                    Init();
                }
                else if (treeView1.SelectedNode.Name.StartsWith("node_machine_"))
                {
                    if (IsAdmin)
                    {
                        var key = treeView1.SelectedNode.Name.Remove(0, 13);
                        await Task.Run(() =>
                        {
                            File.WriteAllText($"Backup{DateTime.Now:yyMMddHHmmss}.txt", $"{key}={machineEnvironmentVariable[key]}");
                            Environment.SetEnvironmentVariable(key, null, EnvironmentVariableTarget.Machine);
                        });
                        Enabled = true;
                        Text = txt;
                        Init();
                    }
                    else
                    {
                        RestartElevated(null);
                    }
                }
            }
        }
    }
}
