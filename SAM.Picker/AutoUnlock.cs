using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace SAM.Picker {
	internal class AutoUnlocker {

		private List<GameInfo> gameInfos;
		private int batchSize;
		private int interval;

		private Timer timer;
		private List<Process> processes = new List<Process>();
		private bool allGamesStarted;
		private bool allGamesEnded;
		private IEnumerator<GameInfo> enumerator;

		private Func<GameInfo, Process> startGameForm;

		public AutoUnlocker(List<GameInfo> gameinfos, int batchSize, int interval, Func<GameInfo, Process> startGameFormFunc) {
			this.gameInfos = gameinfos;
			this.batchSize = batchSize;
			this.interval = interval;
			this.startGameForm = startGameFormFunc;
		}

		~AutoUnlocker() {
			foreach (var process in processes) {
				process.Close();
			}
		}

		public void Start() {
			if (this.timer == null) {
				Init();
			}
			timer.Start();
		}

		public void Stop() {
			timer.Stop();
		}

		private void Init() {
			this.timer = new Timer();
			this.timer.Interval = interval;
			this.timer.Elapsed += Update;

			this.enumerator = this.gameInfos.GetEnumerator();
		}

		private void Update(object sender, EventArgs e) {
			for (int i = this.processes.Count - 1; i >= 0; i--) {
				if (this.processes[i].HasExited) {
					Console.WriteLine($"End {this.processes[i].StartInfo.Arguments[0]}");

					this.processes.RemoveAt(i);
				}
			}

			if (this.allGamesStarted) {
				if (this.processes.Count == 0) {
					this.allGamesEnded = true;
					this.timer.Stop();
					return;
				}
			}

			while (this.processes.Count < this.batchSize) {
				if (this.enumerator.MoveNext()) {
					GameInfo gameInfo = this.enumerator.Current;
					Process p = this.startGameForm.Invoke(gameInfo);
					this.processes.Add(p);

					Console.WriteLine($"Start {gameInfo.Id}, name: {gameInfo.Name}");
				} else {
					this.allGamesStarted = true;
					break;
				}
			}
		}
	}
}
