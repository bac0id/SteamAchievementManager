using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace SAM.Picker {
	internal class AutoUnlocker {

		private List<GameInfo> _gameInfos;
		private SAMGameFactory _gameFactory;
		private int _maxGameProcessCountAtSameTime;
		private int _timerInterval;

		private Timer _timer;
		private List<Process> _activeGameProcesses = new List<Process>();
		private bool _isAllGameProcessesStarted;
		private bool _isAllGameProcessesEnded;
		private IEnumerator<GameInfo> _gameInfosEnumerator;

		public AutoUnlocker(List<GameInfo> gameinfos, SAMGameFactory gameFactory, int maxGameProcessCountAtSameTime = 50, int interval = 200) {
			this._gameInfos = gameinfos;
			this._gameFactory = gameFactory;
			this._maxGameProcessCountAtSameTime = maxGameProcessCountAtSameTime;
			this._timerInterval = interval;
		}

		~AutoUnlocker() {
			foreach (var process in _activeGameProcesses) {
				process.Close();
			}
		}

		public void Start() {
			if (this._timer == null) {
				Init();
			}
			_timer.Start();
		}

		public void Stop() {
			_timer.Stop();
		}

		private void Init() {
			this._timer = new Timer();
			this._timer.Interval = _timerInterval;
			this._timer.Elapsed += Update;

			this._gameInfosEnumerator = this._gameInfos.GetEnumerator();
		}

		private void Update(object sender, EventArgs e) {
			for (int i = this._activeGameProcesses.Count - 1; i >= 0; i--) {
				if (this._activeGameProcesses[i].HasExited) {
					Console.WriteLine($"End {this._activeGameProcesses[i].StartInfo.Arguments[0]}");

					this._activeGameProcesses.RemoveAt(i);
				}
			}

			if (this._isAllGameProcessesStarted) {
				if (this._activeGameProcesses.Count == 0) {
					this._isAllGameProcessesEnded = true;
					this._timer.Stop();
					return;
				}
			}

			while (this._activeGameProcesses.Count < this._maxGameProcessCountAtSameTime) {
				if (this._gameInfosEnumerator.MoveNext()) {
					GameInfo gameInfo = this._gameInfosEnumerator.Current;
					Process p = this._gameFactory.StartGameForm(gameInfo, true);
					this._activeGameProcesses.Add(p);

					Console.WriteLine($"Start {gameInfo.Id}, name: {gameInfo.Name}");
				} else {
					this._isAllGameProcessesStarted = true;
					break;
				}
			}
		}
	}
}
