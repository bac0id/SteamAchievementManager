/* Copyright (c) 2019 Rick (rick 'at' gibbed 'dot' us)
 * 
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 * 
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 * 
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 * 
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 * 
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using System.Xml.XPath;
using APITypes = SAM.API.Types;

namespace SAM.Picker
{
    internal partial class GameListForm : Form
    {
        private readonly API.Client _SteamClient;

        private readonly Dictionary<uint, GameInfo> _Games;
        private readonly List<GameInfo> _gameInfos;
        private int _SelectedGameIndex;

        private readonly List<string> _LogosAttempted;
        private readonly ConcurrentQueue<GameInfo> _LogoQueue;

        // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
        private readonly API.Callbacks.AppDataChanged _AppDataChangedCallback;
        // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable

        public GameListForm(API.Client client)
        {
            this._Games = new Dictionary<uint, GameInfo>();
            this._gameInfos = new List<GameInfo>();
            this._SelectedGameIndex = -1;
            this._LogosAttempted = new List<string>();
            this._LogoQueue = new ConcurrentQueue<GameInfo>();

            this.InitializeComponent();

            var blank = new Bitmap(this._LogoImageList.ImageSize.Width, this._LogoImageList.ImageSize.Height);
            using (var g = Graphics.FromImage(blank))
            {
                g.Clear(Color.DimGray);
            }

            this._LogoImageList.Images.Add("Blank", blank);

            this._SteamClient = client;

            this._AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;

            this.AddGames();
        }

        private void OnAppDataChanged(APITypes.AppDataChanged param)
        {
            if (param.Result == true && this._Games.ContainsKey(param.Id))
            {
                var game = this._Games[param.Id];

                game.Name = this._SteamClient.SteamApps001.GetAppData(game.Id, "name");
                this.AddGameToLogoQueue(game);
                this.DownloadNextLogo();
            }
        }

        private void DoDownloadList(object sender, DoWorkEventArgs e)
        {
            var pairs = new List<KeyValuePair<uint, string>>();
            byte[] bytes;
            using (var downloader = new WebClient())
            {
                bytes = downloader.DownloadData(new Uri("http://gib.me/sam/games.xml"));
            }
            using (var stream = new MemoryStream(bytes, false))
            {
                var document = new XPathDocument(stream);
                var navigator = document.CreateNavigator();
                var nodes = navigator.Select("/games/game");
                while (nodes.MoveNext())
                {
                    string type = nodes.Current.GetAttribute("type", "");
                    if (string.IsNullOrEmpty(type) == true)
                    {
                        type = "normal";
                    }
                    pairs.Add(new KeyValuePair<uint, string>((uint)nodes.Current.ValueAsLong, type));
                }
            }

            foreach (var kv in pairs)
            {
                this.AddGame(kv.Key, kv.Value);
            }
        }

        private void OnDownloadList(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled)
            {
                this.AddDefaultGames();
                //MessageBox.Show(e.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.RefreshGames();
            this._RefreshGamesButton.Enabled = true;
            this.DownloadNextLogo();
        }

        private void RefreshGames()
        {
            this._SelectedGameIndex = -1;
            this._gameInfos.Clear();
            foreach (var info in this._Games.Values.OrderBy(gi => gi.Name))
            {
                if (info.Type == "normal" && _FilterGamesMenuItem.Checked == false)
                {
                    continue;
                }
                if (info.Type == "demo" && this._FilterDemosMenuItem.Checked == false)
                {
                    continue;
                }
                if (info.Type == "mod" && this._FilterModsMenuItem.Checked == false)
                {
                    continue;
                }
                if (info.Type == "junk" && this._FilterJunkMenuItem.Checked == false)
                {
                    continue;
                }
                this._gameInfos.Add(info);
                this.AddGameToLogoQueue(info);
            }

            this._GameListView.VirtualListSize = this._gameInfos.Count;
            this._PickerStatusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Displaying {0} games. Total {1} games.",
                this._GameListView.Items.Count,
                this._Games.Count);

            if (this._GameListView.Items.Count > 0)
            {
                this._GameListView.Items[0].Selected = true;
                this._GameListView.Select();
            }
        }

        private void OnGameListViewRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var info = this._gameInfos[e.ItemIndex];
            e.Item = new ListViewItem()
            {
                Text = info.Name,
                ImageIndex = info.ImageIndex,
            };
        }

        private void OnGameListViewSearchForVirtualItem(object sender, SearchForVirtualItemEventArgs e)
        {
            if (e.Direction != SearchDirectionHint.Down || e.IsTextSearch == false)
            {
                return;
            }

            var count = this._gameInfos.Count;
            if (count < 2)
            {
                return;
            }

            var text = e.Text;
            int startIndex = e.StartIndex;

            Predicate<GameInfo> predicate;
            /*if (e.IsPrefixSearch == true)*/
            {
                predicate = gi => gi.Name != null && gi.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase);
            }
            /*else
            {
                predicate = gi => gi.Name != null && string.Compare(gi.Name, text, StringComparison.CurrentCultureIgnoreCase) == 0;
            }*/

            int index;
            if (e.StartIndex >= count)
            {
                // starting from the last item in the list
                index = this._gameInfos.FindIndex(0, startIndex - 1, predicate);
            }
            else if (startIndex <= 0)
            {
                // starting from the first item in the list
                index = this._gameInfos.FindIndex(0, count, predicate);
            }
            else
            {
                index = this._gameInfos.FindIndex(startIndex, count - startIndex, predicate);
                if (index < 0)
                {
                    index = this._gameInfos.FindIndex(0, startIndex - 1, predicate);
                }
            }

            e.Index = index < 0 ? -1 : index;
        }

        private void DoDownloadLogo(object sender, DoWorkEventArgs e)
        {
            var info = (GameInfo)e.Argument;
            var logoPath = string.Format(
                CultureInfo.InvariantCulture,
                "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/{0}/{1}.jpg",
                info.Id,
                info.Logo);
            using (var downloader = new WebClient())
            {
                try
                {
                    var data = downloader.DownloadData(new Uri(logoPath));
                    using (var stream = new MemoryStream(data, false))
                    {
                        var bitmap = new Bitmap(stream);
                        e.Result = new LogoInfo(info.Id, bitmap);
                    }
                }
                catch (Exception)
                {
                    e.Result = new LogoInfo(info.Id, null);
                }
            }
        }

        private void OnDownloadLogo(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null || e.Cancelled == true)
            {
                return;
            }

            var logoInfo = (LogoInfo)e.Result;
            if (logoInfo.Bitmap != null && this._Games.TryGetValue(logoInfo.Id, out var gameInfo))
            {
                this._GameListView.BeginUpdate();
                var imageIndex = this._LogoImageList.Images.Count;
                this._LogoImageList.Images.Add(gameInfo.Logo, logoInfo.Bitmap);
                gameInfo.ImageIndex = imageIndex;
                this._GameListView.EndUpdate();
            }

            this.DownloadNextLogo();
        }

        private void DownloadNextLogo()
        {
            if (this._LogoWorker.IsBusy == true)
            {
                return;
            }

            GameInfo info;
            if (this._LogoQueue.TryDequeue(out info) == false)
            {
                this._DownloadStatusLabel.Visible = false;
                return;
            }

            this._DownloadStatusLabel.Text = string.Format(
                CultureInfo.CurrentCulture,
                "Downloading {0} game icons...",
                this._LogoQueue.Count);
            this._DownloadStatusLabel.Visible = true;

            this._LogoWorker.RunWorkerAsync(info);
        }

        private void AddGameToLogoQueue(GameInfo info)
        {
            string logo = this._SteamClient.SteamApps001.GetAppData(info.Id, "logo");

            if (logo == null)
            {
                return;
            }

            info.Logo = logo;

            int imageIndex = this._LogoImageList.Images.IndexOfKey(logo);
            if (imageIndex >= 0)
            {
                info.ImageIndex = imageIndex;
            }
            else if (this._LogosAttempted.Contains(logo) == false)
            {
                this._LogosAttempted.Add(logo);
                this._LogoQueue.Enqueue(info);
            }
        }

        private bool OwnsGame(uint id)
        {
            return this._SteamClient.SteamApps008.IsSubscribedApp(id);
        }

        private void AddGame(uint id, string type)
        {
            if (this._Games.ContainsKey(id))
            {
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                return;
            }

            var info = new GameInfo(id, type);
            info.Name = this._SteamClient.SteamApps001.GetAppData(info.Id, "name");

            this._Games.Add(id, info);
        }

        private void AddGames()
        {
            this._Games.Clear();
            this._RefreshGamesButton.Enabled = false;
            this._ListWorker.RunWorkerAsync();
        }

        private void AddDefaultGames()
        {
            this.AddGame(480, "normal"); // Spacewar
        }

        private void OnTimer(object sender, EventArgs e)
        {
            this._CallbackTimer.Enabled = false;
            this._SteamClient.RunCallbacks(false);
            this._CallbackTimer.Enabled = true;
        }

        private void OnSelectGame(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            this._SelectedGameIndex = e.ItemIndex;
        }

        private void OnActivateGame(object sender, EventArgs e)
        {
			var index = this._GameListView.SelectedIndices[0];
            if (index < 0 || index >= this._gameInfos.Count)
            {
                return;
            }

            var gameInfo = this._gameInfos[index];
            if (gameInfo == null)
            {
                return;
            }

            StartGameForm(gameInfo);
        }

        private Process StartGameForm(GameInfo gameInfo) {
            Process process = null;
			try {
				ProcessStartInfo processStartInfo = new ProcessStartInfo();
				processStartInfo.CreateNoWindow = true;
				processStartInfo.FileName = "SAM.Game.exe";
				processStartInfo.Arguments = gameInfo.Id.ToString(CultureInfo.InvariantCulture);

				process = Process.Start(processStartInfo);

				// gameClient.Initialize(info.Id) can cause ClientInitializeException(ClientInitializeFailure.AppIdMismatch, "appID mismatch") in Client.cs

				//var gameClient = new API.Client();
    //            gameClient.Initialize(gameInfo.Id);
    //            new Game.GameForm(gameInfo.Id, gameClient);
			} catch (Win32Exception) {
				MessageBox.Show(
					this,
					"Failed to start SAM.Game.exe.",
					"Error",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
            return process;
		}

        private void OnRefresh(object sender, EventArgs e)
        {
            this._AddGameTextBox.Text = "";
            this.AddGames();
        }

        private void OnAddGame(object sender, EventArgs e)
        {
            uint id;

            string inputStr = this._AddGameTextBox.Text;

			string firstDigitsSubstring = GetFirstContinousDigitSubstring(inputStr);
			if (firstDigitsSubstring == null)
            {
				MessageBox.Show(
	                this,
	                "Please enter a valid game ID.",
	                "Error",
	                MessageBoxButtons.OK,
	                MessageBoxIcon.Error);
				return;
			}

			if (uint.TryParse(firstDigitsSubstring, out id) == false)
            {
                MessageBox.Show(
                    this,
                    "Please enter a valid game ID.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (this.OwnsGame(id) == false)
            {
                MessageBox.Show(this, "You don't own that game.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            while (this._LogoQueue.TryDequeue(out var logo))
            {
                // clear the download queue because we will be showing only one app
                // TODO: https://github.com/gibbed/SteamAchievementManager/issues/106
                this._LogosAttempted.Remove(logo.Logo);
            }

            this._AddGameTextBox.Text = "";
            this._Games.Clear();
            this.AddGame(id, "normal");
            this._FilterGamesMenuItem.Checked = true;
            this.RefreshGames();
            this.DownloadNextLogo();
        }

        private void OnFilterUpdate(object sender, EventArgs e)
        {
            this.RefreshGames();
		}

		private string GetFirstContinousDigitSubstring(string str)
        {
			int length = str.Length;

			char firstDigit = str.FirstOrDefault(x => x >= '0' && x <= '9');
			if (firstDigit == '\0')
            {
                return null;
			}

			int i = str.IndexOf(firstDigit);

			int j = i + 1;
			while (j < length && str[j] >= '0' && str[j] <= '9') 
            {
				++j;
			}

            string ans = str.Substring(i, j - i);
			return ans;
		}

		private void _AutoUnlockButton_Click(object sender, EventArgs e) {
            int batchSize = 50;
            int timerInterval = 200; //ms
			AutoUnlocker autoUnlocker = new AutoUnlocker(_gameInfos, batchSize, timerInterval, StartGameForm);
			autoUnlocker.Start();
		}
	}
}
