using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Shell;
using Ookii.Dialogs.Wpf;

namespace AnonymousLog
{
    public partial class MainWindow : Window
    {
        private class Player : INotifyPropertyChanged
        {
            public Player(string name)
            {
                this.Name = name;
            }

            public string Name { get; }

            private bool isChecked;
            public bool IsChecked
            {
                get => this.isChecked;
                set
                {
                    this.isChecked = value;
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsChecked)));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
        }

        private readonly ObservableCollection<Player> observableCollection = new ObservableCollection<Player>();
        public MainWindow()
        {
            this.InitializeComponent();

            this.CtlList.ItemsSource = this.observableCollection;
        }

        private Stream m_stream;

        private async void CtlRead_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.m_stream?.Dispose();

                this.observableCollection.Clear();
                this.CtlRead.IsEnabled = false;
                this.CtlList.IsEnabled = false;
                this.CtlSave.IsEnabled = false;

                this.CtlTaskbarItem.ProgressState = TaskbarItemProgressState.Normal;
                this.CtlTaskbarItem.ProgressValue = 0;
                this.CtlReadProgress.Value = 0;
                this.CtlReadText.Text = "0 %";

                var dlg = new VistaOpenFileDialog
                {
                    Title = "로그 파일을 선택해주세요",
                    Filter = "로그|*.log",
                    DefaultExt = ".log",
                    AddExtension = true,
                };
                if (!(dlg.ShowDialog() ?? false))
                    return;

                this.m_stream = File.Open(dlg.FileName, FileMode.Open, FileAccess.Read, FileShare.Read);

                using (var reader = new StreamReader(this.m_stream, Encoding.UTF8, true, 64 * 1024, true))
                {
                    var lst = new HashSet<string>();

                    string line;
                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        var p = (double)this.m_stream.Position / this.m_stream.Length;
                        this.CtlTaskbarItem.ProgressValue = p;
                        this.CtlReadProgress.Value = p * 100;
                        this.CtlReadText.Text = $"{p * 100:##0} %";

                        //////////////////////////////////////////////////
                        
                        if (!line.StartsWith("03|")) continue;

                        var ss = line.Split('|');
                        var name = ss.Length > 3 ? ss[3] : null;
                        var server = ss.Length > 8 ? ss[8] : null;

                        if (!string.IsNullOrWhiteSpace(server) && !string.IsNullOrWhiteSpace(name))
                        {
                            _ = lst.Add(name);
                        }

                        //////////////////////////////////////////////////
                    }

                    this.CtlTaskbarItem.ProgressValue = 1;
                    this.CtlReadProgress.Value = 100;
                    this.CtlReadText.Text = "100 %";

                    if (lst.Count > 0)
                    {
                        foreach (var name in lst.OrderBy(le => le))
                        {
                            this.observableCollection.Add(new Player(name));
                        }

                        this.CtlList.IsEnabled = true;
                        this.CtlSave.IsEnabled = true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"알 수 없는 오류가 발생하였습니다\n\n{ex.Message}", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.CtlTaskbarItem.ProgressState = TaskbarItemProgressState.None;
                this.CtlReadProgress.Value = 0;

                this.CtlRead.IsEnabled = true;
                this.CtlReadText.Text = "열기";
            }
        }

        private async void CtlSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.CtlRead.IsEnabled = false;
                this.CtlList.IsEnabled = false;
                this.CtlSave.IsEnabled = false;

                this.CtlTaskbarItem.ProgressState = TaskbarItemProgressState.Normal;
                this.CtlTaskbarItem.ProgressValue = 0;
                this.CtlSaveProgress.Value = 0;
                this.CtlSaveText.Text = "0 %";

                var selected = this.observableCollection.
                    Where(le => le.IsChecked).
                    Select(le => le.Name).
                    Select(le =>
                    {
                        uint hash = 0;
                        foreach (var b in  Encoding.UTF8.GetBytes(le))
                        {
                            hash *= 0x811C9DC5;
                            hash ^= b;
                        }

                        //return (le, $"Anonymous{hash & 0xFFFF:x04}");
                        return (le, $"Anonymous");
                    }).
                    ToDictionary(le => le.le, le => le.Item2);

                if (selected.Count == 0) return;

                var dlg = new VistaSaveFileDialog
                {
                    Title = "저장할 로그 파일 위치를 선택해주세요",
                    Filter = "로그 파일|*.log",
                    DefaultExt = ".log",
                    AddExtension = true,
                };
                if (!(dlg.ShowDialog() ?? false))
                    return;

                using (var saveStream = File.CreateText(dlg.FileName))
                {
                    using (var md5 = MD5.Create())
                    {
                        this.m_stream.Position = 0;
                        using (var reader = new StreamReader(this.m_stream, Encoding.UTF8, true, 64 * 1024, true))
                        {
                            var sb = new StringBuilder();

                            var lineNum = 0;

                            string line;
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                var p = (double)this.m_stream.Position / this.m_stream.Length;
                                this.CtlTaskbarItem.ProgressValue = p;
                                this.CtlSaveProgress.Value = p * 100;
                                this.CtlSaveText.Text = $"{p * 100:##0} %";

                                //////////////////////////////////////////////////
                                //if (line.StartsWith("00|")) continue;

                                lineNum++;

                                var m = reLine.Match(line);
                                if (m.Success && int.TryParse(m.Groups["code"].Value, out var code))
                                {
                                    if (code == 253 || code == 1)
                                    {
                                        lineNum = 1;
                                    }

                                    _ = sb.Clear();
                                    _ = sb.Append(m.Groups["code"].ToString());
                                    _ = sb.Append('|');
                                    _ = sb.Append(m.Groups["date"].ToString());
                                    _ = sb.Append('|');

                                    if (NameIndexes.TryGetValue(code, out var indexes))
                                    {
                                        var lineBody = m.Groups["body"].Value;


                                        string part;
                                        var currentIndex = 0;

                                        var indexesIndex = 0;

                                        while (!string.IsNullOrEmpty(lineBody))
                                        {
                                            var cur = lineBody.IndexOf('|');
                                            if (cur == -1)
                                            {
                                                part = lineBody;
                                                lineBody = null;
                                            }
                                            else
                                            {
                                                part = lineBody.Substring(0, cur);
                                                lineBody = lineBody.Substring(cur + 1);
                                            }

                                            if (indexesIndex < indexes.Length && currentIndex == indexes[indexesIndex])
                                            {
                                                indexesIndex++;

                                                if (selected.TryGetValue(part, out var newName))
                                                {
                                                    _ = sb.Append(newName);
                                                    _ = sb.Append("|");
                                                }
                                                else
                                                {
                                                    _ = sb.Append(part);
                                                    _ = sb.Append("|");
                                                }
                                            }
                                            else
                                            {
                                                _ = sb.Append(part);
                                                _ = sb.Append("|");
                                            }
                                            currentIndex++;
                                        }
                                    }
                                    else
                                    {
                                        _ = sb.Append(m.Groups["body"].Value);
                                    }

                                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{sb}{lineNum}"));
                                    foreach (var b in hash)
                                    {
                                        _ = sb.Append(b.ToString("x02"));
                                    }

                                    line = sb.ToString();
                                }

                                await saveStream.WriteLineAsync(line);

                                //////////////////////////////////////////////////
                            }

                            this.CtlTaskbarItem.ProgressValue = 1;
                            this.CtlSaveProgress.Value = 100;
                            this.CtlSaveText.Text = "100 %";
                        }
                    }
                }

                MessageBox.Show(this, $"저장되었습니다.", this.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"알 수 없는 오류가 발생하였습니다\n\n{ex.Message}", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.CtlTaskbarItem.ProgressState = TaskbarItemProgressState.None;
                this.CtlSaveProgress.Value = 0;

                this.CtlRead.IsEnabled = true;
                this.CtlList.IsEnabled = true;
                this.CtlSave.IsEnabled = true;

                this.CtlSaveText.Text = "저장";
            }
        }

        private void CtlCopyRight_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = "\"https://github.com/RyuaNerin\"" }))
                {
                }
            }
            catch
            {
            }
        }

        static readonly Regex reLine = new Regex(@"^(?<code>[0-9a-f]+)\|(?<date>.+?)\|(?<body>.+\|)(?<hash>.+?)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static readonly Dictionary<int, byte[]> NameIndexes = new Dictionary<int, byte[]>
        {
            {  0, new byte[] { 1,   } }, // LogLine              FormatChatMessage
            
            {  2, new byte[] { 1,   } }, // ChangePrimaryPlayer  FormatChangePrimaryPlayerMessage
            
            {  3, new byte[] { 1,   } }, // AddCombatant         FormatCombatantMessage
            {  4, new byte[] { 1,   } }, // RemoveCombatant      FormatCombatantMessage
            
            { 37, new byte[] { 1,   } }, // NetworkEffectResult  FormatEffectResultMessage
            
            { 10, new byte[] { 1,   } }, // IncomingAbility      FormatIncomingAbilityMessage
            
            {  5, new byte[] { 2, 4 } }, // AddBuff              FormatMemoryBuffMessage
            {  6, new byte[] { 2, 4 } }, // RemoveBuff           FormatMemoryBuffMessage

            { 21, new byte[] { 1, 5 } }, // NetworkAbility       FormatNetworkAbilityMessage
            { 22, new byte[] { 1, 5 } }, // NetworkAOEAbility    FormatNetworkAbilityMessage

            { 26, new byte[] { 4, 6 } }, // NetworkBuff          FormatNetworkBuffMessage
            { 30, new byte[] { 4, 6 } }, // NetworkBuffRemove    FormatNetworkBuffMessage

            { 23, new byte[] { 1    } }, // NetworkCancelAbility FormatNetworkCancelMessage

            { 20, new byte[] { 1, 5 } }, // NetworkStartsCasting FormatNetworkCastMessage

            { 25, new byte[] { 1, 3 } }, // NetworkDeath         FormatNetworkDeathMessage

            { 24, new byte[] { 1    } }, // NetworkDoT           FormatNetworkDoTMessage

            { 29, new byte[] { 3, 5 } }, // NetworkSignMarker    FormatNetworkSignMessage

            { 27, new byte[] { 1    } }, // NetworkTargetIcon    FormatNetworkTargetIconMessage

            { 34, new byte[] { 1, 3 } }, // NetworkNameToggle    FormatNetworkTargettableMessage

            { 35, new byte[] { 1, 3 } }, // NetworkTether        FormatNetworkTetherMessage

            { 28, new byte[] { 3    } }, // NetworkWaymarkMarker FormatNetworkWaymarkMessage

            { 38, new byte[] { 1    } }, // NetworkStatusList    FormatStatusListMessage

            { 39, new byte[] { 1    } }, // NetworkUpdateHp      FormatUpdateHpMpTp
        };
    }
}
