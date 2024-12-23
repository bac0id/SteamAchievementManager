using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace SAM.Picker {
	internal class SAMGameFactory {
		public Process StartGameForm(GameInfo gameInfo, bool enableAutoUnlock) {
			Process process = null;
			try {
				ProcessStartInfo processStartInfo = new ProcessStartInfo();
				processStartInfo.CreateNoWindow = true;
				processStartInfo.FileName = "SAM.Game.exe";
				processStartInfo.Arguments = $"{gameInfo.Id.ToString(CultureInfo.InvariantCulture)} {(enableAutoUnlock ? 1 : 0)}";
				process = Process.Start(processStartInfo);

				// gameClient.Initialize(info.Id) can cause ClientInitializeException(ClientInitializeFailure.AppIdMismatch, "appID mismatch") in Client.cs

				//var gameClient = new API.Client();
				//gameClient.Initialize(gameInfo.Id);
				//new Game.GameForm(gameInfo.Id, gameClient);
			} catch (Win32Exception) {
				MessageBox.Show(
					"Failed to start SAM.Game.exe.",
					"Error",
					MessageBoxButtons.OK,
					MessageBoxIcon.Error);
			}
			return process;
		}
	}
}
