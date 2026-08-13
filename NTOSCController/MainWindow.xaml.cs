using System;
using System.Windows;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows.Threading;
using System.Net.Sockets;
using System.Net;
using System.IO; // ファイル出力用に追加

namespace NanoTerasuOSCController
{
    public partial class MainWindow : Window
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly NanoTerasuOscController _oscController;
        private readonly HttpClient _httpClient;
        private readonly DispatcherTimer _updateTimer;

        private UdpClient? _oscReceiver;

        private bool _isAutoSyncEnabled = false;
        private bool _isAvatarActive = false;
        private AppConfig _appConfig = new AppConfig();

        public MainWindow()
        {
            InitializeComponent();

            LoadConfig();

            _oscController = new NanoTerasuOscController("127.0.0.1", 9000);
            _httpClient = new HttpClient();

            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Information,
                Visible = true,
                Text = "NanoTerasu OSC"
            };

            _notifyIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = WindowState.Normal;
            };

            var contextMenu = new ContextMenuStrip();
            var openMenu = new ToolStripMenuItem("設定画面を開く");
            openMenu.Click += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; };
            var exitMenu = new ToolStripMenuItem("終了");
            exitMenu.Click += (s, e) => System.Windows.Application.Current.Shutdown();

            contextMenu.Items.Add(openMenu);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitMenu);
            _notifyIcon.ContextMenuStrip = contextMenu;

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5)
            };
            _updateTimer.Tick += async (s, e) => await FetchAndUpdateStatusAsync(forceSync: false);

            StartOscListener();
        }

        private void BtnToggleAutoSync_Click(object sender, RoutedEventArgs e)
        {
            SetAutoSyncState(!_isAutoSyncEnabled, sendToVrc: true);
        }

        private void SetAutoSyncState(bool isEnabled, bool sendToVrc)
        {
            Dispatcher.Invoke(() =>
            {
                _isAutoSyncEnabled = isEnabled;

                if (_isAutoSyncEnabled)
                {
                    BtnToggleAutoSync.Content = "自動同期: ON (VRChat連動中)";
                    BtnToggleAutoSync.Background = System.Windows.Media.Brushes.LightGreen;
                    _updateTimer.Start();

                    _ = FetchAndUpdateStatusAsync(forceSync: false);
                }
                else
                {
                    BtnToggleAutoSync.Content = "自動同期: OFF (手動モード)";
                    BtnToggleAutoSync.Background = System.Windows.Media.Brushes.LightPink;
                    _updateTimer.Stop();
                }
            });

            if (sendToVrc)
            {
                _oscController.SendBool("NT_Sync_Enable", _isAutoSyncEnabled);
            }
        }

        private void StartOscListener()
        {
            try
            {
                _oscReceiver = new UdpClient(9001);
                _oscReceiver.BeginReceive(new AsyncCallback(OscReceiveCallback), null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OSC受信ポートのバインドに失敗: {ex.Message}");
            }
        }

        private void OscReceiveCallback(IAsyncResult ar)
        {
            if (_oscReceiver == null) return;

            try
            {
                IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 9001);
                byte[] bytes = _oscReceiver.EndReceive(ar, ref endpoint);

                string dataStr = System.Text.Encoding.ASCII.GetString(bytes);

                // ==========================================
                // 【デバッグ用】パケットダンプ出力
                // ==========================================
#if DEBUG
                try
                {
                    string hexStr = BitConverter.ToString(bytes).Replace("-", " ");
                    // \0 (Null文字) を [NUL] に置換して可視化する
                    string safeAscii = dataStr.Replace("\0", "[NUL]");

                    string logText = $"[{DateTime.Now:HH:mm:ss.fff}] Received {bytes.Length} bytes\n" +
                                     $"ASCII: {safeAscii}\n" +
                                     $"HEX  : {hexStr}\n" +
                                     new string('-', 50) + "\n";

                    File.AppendAllText("osc_debug_dump.txt", logText);
                    System.Diagnostics.Debug.WriteLine(logText);
                }
                catch { }
#endif
                // ==========================================

                bool avatarJustActivated = false;

                if (dataStr.Contains("/avatar/change"))
                {
                    if (_isAvatarActive)
                    {
                        _isAvatarActive = false;
                        Dispatcher.Invoke(UpdateAvatarStatusUI);
                    }
                }
                else if (dataStr.Contains("/avatar/parameters/NT_"))
                {
                    if (!_isAvatarActive)
                    {
                        _isAvatarActive = true;
                        avatarJustActivated = true;
                        Dispatcher.Invoke(UpdateAvatarStatusUI);
                    }

                    int syncIndex = dataStr.IndexOf("/avatar/parameters/NT_Sync_Enable");
                    if (syncIndex != -1)
                    {
                        int startIndex = syncIndex + "/avatar/parameters/NT_Sync_Enable".Length;
                        int lengthToSearch = Math.Min(12, dataStr.Length - startIndex);
                        string localChunk = dataStr.Substring(startIndex, lengthToSearch);

                        bool? vrcMode = null;
                        if (localChunk.Contains(",T")) vrcMode = true;
                        else if (localChunk.Contains(",F")) vrcMode = false;

                        if (vrcMode.HasValue && _isAutoSyncEnabled != vrcMode.Value)
                        {
                            SetAutoSyncState(vrcMode.Value, sendToVrc: false);
                            avatarJustActivated = false;
                        }
                    }

                    if (avatarJustActivated && _isAutoSyncEnabled)
                    {
                        _ = FetchAndUpdateStatusAsync(forceSync: false);
                    }
                }

                _oscReceiver.BeginReceive(new AsyncCallback(OscReceiveCallback), null);
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OSC受信エラー: {ex.Message}");
            }
        }

        private void UpdateAvatarStatusUI()
        {
            TxtAvatarStatus.Text = _isAvatarActive ? "アバター状態: ナノテラス装備中" : "アバター状態: 未検知 (着替え・待機中)";
            TxtAvatarStatus.Foreground = _isAvatarActive ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Gray;
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized) this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateTimer.Stop();
            _oscReceiver?.Close();
            _httpClient.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            base.OnClosed(e);
        }

        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            await FetchAndUpdateStatusAsync(forceSync: true);
        }

        private void BtnAllOn_Click(object sender, RoutedEventArgs e) => _oscController.SendTrigger("NT_ALL_ON");
        private void BtnAllOff_Click(object sender, RoutedEventArgs e) => _oscController.SendTrigger("NT_ALL_OFF");

        private async Task FetchAndUpdateStatusAsync(bool forceSync)
        {
            if (!forceSync)
            {
                if (!_isAutoSyncEnabled) return;

                if (!_isAvatarActive)
                {
                    Dispatcher.Invoke(() => TxtLastSync.Text = $"同期待機中 (アバター未検知): {DateTime.Now:HH:mm:ss}");
                    return;
                }
            }

            try
            {
                string dataJson = await _httpClient.GetStringAsync(_appConfig.DataFileUrl);
                using (JsonDocument doc = JsonDocument.Parse(dataJson))
                {
                    var root = doc.RootElement;
                    int srStat = GetStatusValue(root, "safety_intlk_sr_di_systemkey/status");
                    int linStat = GetStatusValue(root, "safety_intlk_li_di_systemkey/status");
                    int btStat = GetStatusValue(root, "nt_connection");

                    _oscController.SendInt("NT_SR_Stat", srStat);
                    _oscController.SendInt("NT_Lin_Stat", linStat);
                    _oscController.SendInt("NT_BT_Stat", btStat);
                }

                string mbsJson = await _httpClient.GetStringAsync(_appConfig.MbsStatusUrl);
                using (JsonDocument doc = JsonDocument.Parse(mbsJson))
                {
                    var root = doc.RootElement;
                    var beamlineKeys = new Dictionary<string, string>
                    {
                        {"NT_BL02U_Stat", "bl_fe_02u_mbs_1/status"}, {"NT_BL06U_Stat", "bl_fe_06u_mbs_1/status"},
                        {"NT_BL07U_Stat", "bl_fe_07u_mbs_1/status"}, {"NT_BL08U_Stat", "bl_fe_08u_mbs_1/status"},
                        {"NT_BL08W_Stat", "bl_fe_08w_mbs_1/status"}, {"NT_BL09U_Stat", "bl_fe_09u_mbs_1/status"},
                        {"NT_BL09W_Stat", "bl_fe_09w_mbs_1/status"}, {"NT_BL10U_Stat", "bl_fe_10u_mbs_1/status"},
                        {"NT_BL13U_Stat", "bl_fe_13u_mbs_1/status"}, {"NT_BL14U_Stat", "bl_fe_14u_mbs_1/status"}
                    };

                    foreach (var bl in beamlineKeys)
                    {
                        int stat = GetStatusValue(root, bl.Value);
                        _oscController.SendInt(bl.Key, stat);
                    }
                }

                Dispatcher.Invoke(() => TxtLastSync.Text = $"最終同期: {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"データ取得エラー: {ex.Message}");
                Dispatcher.Invoke(() => TxtLastSync.Text = $"同期失敗: {DateTime.Now:HH:mm:ss}");
            }
        }

        private int GetStatusValue(JsonElement root, string keyName)
        {
            if (root.TryGetProperty(keyName, out JsonElement keyElement) &&
                keyElement.TryGetProperty("res", out JsonElement resElement) && resElement.GetArrayLength() > 0)
            {
                var resArray = resElement[0];
                if (resArray.GetArrayLength() > 1 && resArray[1].TryGetInt32(out int value))
                {
                    return value;
                }
            }
            return 0;
        }
        private void LoadConfig()
        {
            string configPath = "config.json";

            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    _appConfig = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

                    // URLが空の場合は警告を出す
                    if (string.IsNullOrWhiteSpace(_appConfig.DataFileUrl) ||
                        string.IsNullOrWhiteSpace(_appConfig.MbsStatusUrl))
                    {
                        System.Windows.MessageBox.Show("config.json にURLが正しく設定されていません。", "設定エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                        Environment.Exit(1);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"config.json の読み込みに失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(1);
                }
            }
            else
            {
                // ファイルが存在しない場合は、空の雛形ファイルを作成する
                var defaultConfig = new AppConfig
                {
                    DataFileUrl = "YOUR_DATA_FILE_URL_HERE",
                    MbsStatusUrl = "YOUR_MBS_STATUS_URL_HERE"
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, JsonSerializer.Serialize(defaultConfig, options));

                System.Windows.MessageBox.Show("設定ファイル (config.json) が見つからなかったため、新しく作成しました。\nURLを入力してからアプリを再起動してください。", "初期設定", MessageBoxButton.OK, MessageBoxImage.Information);
                Environment.Exit(0);
            }
        }
    }
    public class AppConfig
    {
        public string DataFileUrl { get; set; } = "";
        public string MbsStatusUrl { get; set; } = "";
    }
}