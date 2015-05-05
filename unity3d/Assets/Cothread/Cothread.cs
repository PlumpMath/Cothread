#region Header
/**
 *  Cothread for unity3d
 *  Author: seewind
 *  https://github.com/seewindcn/unity-async
 *  email: seewindcn@gmail.com
 **/
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;


namespace Cothreads {
	public class CothreadError : Exception {
		public string Msg;
		public CothreadError(string msg) {
			Msg = msg;
		}

		public override string ToString() {
			return Msg;
		}
	}

	public class CothreadTimeoutError : CothreadError
	{
		public CothreadTimeout Timeout;
		public CothreadTimeoutError(CothreadTimeout t):base("Timeout")
		{
			Timeout = t;
		}
	}

	public sealed class CothreadResult<T>
	{
		public T Result;
		public Exception Error=null;

		public T GetResult(bool isThrowTimeout=false)
		{
			if (Error != null)
			{
				throw Error;
			}
			if (CothreadHub.Instance.CurrentCothread.IsTimeout(isThrowTimeout)) return default(T);
			return Result;
		}
	}

	public class YieldState 
	{
		public IEnumerator IE { get; set;} 
		public uint WakeTime;
		
		public YieldState(IEnumerator ie, uint wakeTime) {
			IE = ie;
			WakeTime = wakeTime;
		}
		
		public override string ToString() {
			return string.Format("Cothread.YieldState<{0}>(wakeTime={1}", 
			                     GetHashCode(), WakeTime);
		}
	}

	public class CoHelper
	{
		public static Cothread StartCoroutine(IEnumerator routine)
		{
			return CothreadHub.Instance.StartCoroutine(routine);
		}

		public static Cothread StartCoroutine(uint ms, IEnumerator routine)
		{
			return CothreadHub.Instance.StartCoroutine(ms, routine);
		}

		public static List<Cothread> StartCoroutines(List<IEnumerator> ies)
		{
			var rs = new List<Cothread>();
			foreach (var ie in ies)
			{
				rs.Add(CothreadHub.Instance.StartCoroutine(ie));
			}
			return rs;
		}

		public static IEnumerator Joins(List<Cothread> threads)
		{
			foreach (var thread in threads)
			{
				yield return thread.Join();
			}
		}

		public static void Joins(List<Cothread> threads, Action act)
		{
			StartCoroutine(Joins(threads)).EndCallBack(act);
		}

		public static IEnumerator StartThread(Action action)
		{
			return CothreadHub.Instance.StartThread(action);
		}

		public static IEnumerator Sleep(uint ms)
		{
			return CothreadHub.Instance.Sleep(ms);
		}

		public static Cothread StartIEnumerators(params object[] ies)
		{
			return StartCoroutine(yieldIEnumerators(ies));
		}

		public static Cothread StartIEnumerators(List<object> ies)
		{
			return StartCoroutine(yieldIEnumerators(ies.ToArray()));
		}

		public static IEnumerator yieldIEnumerators(object[] ies)
		{
			foreach (var ie in ies)
			{
				yield return ie;
			}
		}

	}

	public class CothreadHub {
		#region 属性定义
		[ThreadStatic]
		public static CothreadHub Instance;

		public static object YIELD_CALLBACK = new object();

		public static Action<object> LogHandler;

		public float IdleTickTime = 0.1f;
		public float BusyTickTime = 0.01f;

		public bool Stoped { get; set; }

		public int MainThreadID {
			get { return MainThreadID;}
		}
		protected int mainThreadID = Thread.CurrentThread.ManagedThreadId;

		public Cothread CurrentCothread {
			get { return GetCothread(current); }
		}

		private Stopwatch swTime = new Stopwatch();
		private uint passTime=0;
		internal protected IEnumerator current;
		internal protected AsyncCallback cb;
		protected object locker = new object();
		Dictionary<IEnumerator, Cothread> threads = new Dictionary<IEnumerator, Cothread>();
		List<IEnumerator> yields = new List<IEnumerator>();
		protected List<YieldState> times = new List<YieldState>();
		int cbIndex;

		#endregion

		public CothreadHub() {
			Stoped = true;
			cb = new AsyncCallback(callBack);
		}

		#region 公共方法
		public Cothread GetCothread(IEnumerator ie) {
			if (threads.ContainsKey(ie))
				return threads[ie];
			return null;
		}

		public void Start() {
			Stoped = false;
		}
		public void Stop() {
			Stoped = true;
		}

		Cothread startCoroutine(IEnumerator routine) {
			var th = new Cothread();
			var iee = callIEnumerable(routine, th);
			var ie = iee.GetEnumerator();
			th.IE = ie;
			threads[ie] = th;
			return th;
		}

		public Cothread StartCoroutine(IEnumerator routine) {
			var th = startCoroutine(routine);
			addCothread(th.IE);
			return th;
		}

		public Cothread StartCoroutine(uint ms, IEnumerator routine) {
			var th = startCoroutine(routine);
			addTimeUp(ms, th.IE);
			return th;
		}

		IEnumerator invoke(Action act)
		{
			act();
			yield return null;
		}

		public Cothread Invoke(uint ms, Action act)
		{
			return StartCoroutine(ms, invoke(() => act()));
		}
		public Cothread Invoke<T>(uint ms, Action<T> act, T param)
		{
			return StartCoroutine(ms, invoke(() => act(param)));
		}
		public Cothread Invoke<T1, T2>(uint ms, Action<T1, T2> act, T1 param1, T2 param2)
		{
			return StartCoroutine(ms, invoke(() => act(param1, param2)));
		}


		public IEnumerator StartThread(Action action) {
			IAsyncResult rs = action.BeginInvoke(delegate(IAsyncResult ar) {
				action.EndInvoke(ar);
				cb(ar);
			}, 
			current);

			if (!rs.IsCompleted) yield return rs;
			else yield return null;
		}

		public IEnumerator Sleep(uint ms) {
			if (current == null) {
				throw new CothreadError("hup.Sleep only can be call from coroutine");
			}
			addTimeUp(ms, current);
			yield return CothreadHub.YIELD_CALLBACK;
		}

		protected internal IEnumerator Join(IEnumerator ie, uint ms) {
			var th = GetCothread(ie);
			if (th == null)
				return null;
			return th.Join(ms);
		}

		public static void Log(object msg) {
			if (LogHandler != null)
				LogHandler(msg);
		}

		public double Tick() {
			var len = yields.Count;
			IEnumerator ie;
			for (int i=0; i < len; i++) {
				ie = popYield();
				if (ie == null) 
					break;
				call(ie);
			}

			YieldState state;
			passTime += (uint)swTime.ElapsedMilliseconds;
			swTime.Reset();
			swTime.Start();
			while (times.Count > 0 && times[0].WakeTime <= passTime) {
				state = times[0]; 
				times.RemoveAt(0);
				call(state.IE);
			}

			double sleepTime;
			if (yields.Count > 0)
				sleepTime = BusyTickTime;
			else
				sleepTime = IdleTickTime;
			if (times.Count > 0)
				sleepTime = Math.Min(sleepTime, (times[0].WakeTime - passTime) / (double)TimeSpan.TicksPerMillisecond);

			return sleepTime;
		}

		#endregion

		#region 私有方法
		protected int nextIndex() {
			cbIndex += 1;
			return cbIndex;
		}

		protected internal Cothread delCothread(IEnumerator ie) {
			if (threads.ContainsKey(ie)) {
				var rs = threads[ie];
				threads.Remove(ie);
				return rs;
			}
			return null;
		}

		private void callBack(IAsyncResult ar) {
			lock (locker) {
				var ie = (IEnumerator)ar.AsyncState;
				var th = GetCothread(ie);
				if (th.AsyncResult != ar) {
					Log("****[Hub.callBack]"+ar.ToString()+"******");
					return;
				}

				th.AsyncResult = null;
				addCothread(ie);
			}
		}

		internal protected void addCothread(IEnumerator ie) {
			lock (locker) {
				var th = GetCothread(ie);
				if (th != null) 
					th.AsyncResult = null;
				yields.Add(ie);
			}
		}

		internal protected void addCothreads(List<IEnumerator> ies) {
			lock (locker) {
				Cothread th;
				foreach (var ie in ies) {
					th = GetCothread(ie);
					if (th != null)
						th.AsyncResult = null;
					yields.Add(ie);
				}
			}
		}

		protected IEnumerator popYield() {
			lock (locker) {
				if (yields.Count > 0) 
				{
					var rs = yields[0];
					yields.RemoveAt(0);
					return rs;
				}
				return null;
			}
		}

		public void addCallback(IEnumerator ie) {
			lock (locker) {
				var th = GetCothread(ie);
				if (th == null) {
					return;
				}
				if (th.AsyncResult != null) {
					throw new CothreadError("addCallback error: yield exist");
				}
			}
			return;
		}

		static int _sortTimes(YieldState y1, YieldState y2) {
			return y1.WakeTime.CompareTo(y2.WakeTime);
			/*
			if (y1.WakeTime < y2.WakeTime)
				return -1;
			if (y1.WakeTime > y2.WakeTime)
				return 1;
			return 0;
			*/
		}

		internal protected YieldState addTimeUp(uint time, IEnumerator ie) {
			var state = new YieldState(ie, passTime + time);
			times.Add(state);
			times.Sort(_sortTimes);
			return state;
		}

		protected void call(IEnumerator ie) {
			current = ie;
			var ok = ie.MoveNext();
			current = null;
			var th = GetCothread(ie);
			if (!ok) {
				if (th != null) {
					th.Close();
					delCothread(ie);
				}
				return;
			}

			if (ie.Current == CothreadHub.YIELD_CALLBACK) addCallback(ie);
			else if (ie.Current is IAsyncResult) {
				addCallback(ie);
				th.AsyncResult = ie.Current;
			} else if (ie.Current is Cothread) {
				var th1 = (Cothread)ie.Current;
				if (th1.Closed || !th1.ev.add(ie)) addCothread(ie);
			} else if (ie.Current is CothreadEvent)
			{
				var ev = (CothreadEvent)ie.Current;
				if (!ev.Seted) ev.add(ie);
				else addCothread(ie);
			} 
			else addCothread(ie);
		}

		#endregion


		#region 静态
		public delegate object AsyncHandler(IEnumerator ie);
		public static AsyncHandler GlobalAsyncHandle;

		static IEnumerable callIEnumerable(IEnumerable iee, Cothread th) {
			return callIEnumerable(iee.GetEnumerator(), th);
		}

		static IEnumerable callIEnumerable(IEnumerator ie, Cothread th) {
			IEnumerable rss;
			bool ok;
			var hub = CothreadHub.Instance;
			while (true) {
				//check thread is closed?
				if (!hub.threads.ContainsKey(th.IE)) {
					yield break;
				}

				ok = false;
				try {
					ok = ie.MoveNext();
				} catch (Exception err)
				{
					ok = false;
					CothreadHub.Log(err);
				}
				if (!ok)
					yield break;

				rss = null;
				if (ie.Current is CothreadEvent) {}
				else if (ie.Current is IEnumerable)
					rss = callIEnumerable(ie.Current as IEnumerable, th);
				else if (ie.Current is IEnumerator) 
					rss = callIEnumerable(ie.Current as IEnumerator, th);
				else if (GlobalAsyncHandle != null) {
					var o1 = GlobalAsyncHandle(ie);
					if (o1 is IEnumerable)
						rss = o1 as IEnumerable;
				}

				if (rss != null) {
					foreach (var i in rss)
						yield return i;
				} else
					yield return ie.Current;
			}
		}

		#endregion
	}

	public class NormalHub: CothreadHub {
		public static CothreadHub Install() {
			if (Instance != null) 
				return Instance;
			Instance = new NormalHub();
			return Instance;
		}

		public void Loop() {
			Stoped = false;
			while (!Stoped) {
				double sleepTime = Tick();
				Thread.Sleep((int)(sleepTime * TimeSpan.TicksPerSecond));
			}
		}
	}

	public class Cothread {
		public IEnumerator IE { get; set;}
		public bool Closed { get; set; }
		public object AsyncResult;
		public CothreadTimeout Timeout;

		private CothreadEvent _ev;
		protected internal CothreadEvent ev {
			get {
				if (_ev == null)
					_ev = new CothreadEvent();
				return _ev;
			}
		}

		private Hashtable _locals;
		public Hashtable Locals
		{
			get
			{
				if (_locals == null) _locals = new Hashtable();
				return _locals;
			}
		}

		public Cothread() {
			Closed = false;
		}

		~Cothread() {
			Close();
		}

		public void Close() {
			if (Closed) return;
			Closed = true;
			if (_ev != null) {
				try
				{
					_ev.Set(this);
				}
				catch (Exception err)
				{
					CothreadHub.Log(err);
				}
				_ev = null;
			}
			CothreadHub.Instance.delCothread(IE);
		}

		public IEnumerator Join(uint ms=0) {
			if (Closed) {
				return null;
			}
			return ev.Wait(ms);
		}

		public bool IsTimeout(bool isThrowTimeout)
		{
			var t = Timeout;
			Timeout = null;
			if (t != null)
			{
				if (isThrowTimeout) throw new CothreadTimeoutError(t);
				return true;
			}
			return false;
		}

		public void EndCallBack(Action act)
		{
			ev.onSet0 += act;
		}
	}


	public class CothreadEvent: IEnumerator {
		public object Current { get; set;}
		public Action<object> onSet;
		public Action onSet0;

		private static object NULL = new object();
		private List<IEnumerator> yields = new List<IEnumerator>();

		public bool Seted
		{
			get { return Current != NULL; }
		}

		public CothreadEvent() {
			Current = NULL;
		}

		public CothreadEvent(object v) {
			Current = v;
		}

		protected internal bool add(IEnumerator ie) {
			if (Current != NULL) return false;
			yields.Add(ie);
			return true;
		}

		public void Clear() {
			Current = NULL;
			yields.Clear();
		}

		public bool MoveNext() {
			var hub = CothreadHub.Instance;
			if (hub.current != this)
				throw new CothreadError("Can no call from other Cothread");
			hub.addCothreads(yields);
			yields.Clear();
			return false;
		}

		public void Reset() {
			Clear();
		}

		public IEnumerator Wait(uint ms=0) {
			if (Current != NULL) {
				yield return Current;
				yield break;
			}
			CothreadTimeout t=null;
			yields.Add(CothreadHub.Instance.current);
			if (ms > 0)
				t = CothreadTimeout.NewWithStart(ms);
			yield return CothreadHub.YIELD_CALLBACK;
			if (ms > 0)
				t.Cancel(false);
		}

		public object Get(object defaultValue=null) {
			if (Current != NULL) 
				return Current;
			return defaultValue;
		}

		public void Set(object v=null) {
			Current = v;
			if (yields.Count > 0) 
				CothreadHub.Instance.addCothread(this);
			if (onSet != null) onSet(v);
			if (onSet0 != null) onSet0();
		}
	}


	public class CothreadTimeout: IEnumerator {
		public object Current { get { return Timeout; } }
		public bool Timeout { get; set; }
		public uint TimeoutTime;
		public TimeSpan PassTime { get { return (DateTime.Now - startTime);} }

		private IEnumerator ie;
		private bool cancel;
		private DateTime startTime;
		private YieldState state;

		public static CothreadTimeout NewWithStart(uint ms) {
			var rs = new CothreadTimeout();
			rs.Start(ms);
			return rs;
		}

		public bool MoveNext() {
			if (CothreadHub.Instance.current != this) 
				throw new CothreadError("CothreadTimeout.MoveNext Can not call from other");
			if (cancel) 
				return false;

			Timeout = true;
			var hub = CothreadHub.Instance;
			var th = hub.GetCothread(ie);
			if (th == null) 
				return false;
			th.Timeout = this;
			hub.addCothread(ie);
			return false;
		}

		public void Reset() {
			Cancel(true);
		}

		public void Start(uint ms) {
			cancel = false;
			var hub = CothreadHub.Instance;
			hub.CurrentCothread.Timeout = null;
			ie = hub.current;
			TimeoutTime = ms;
			startTime = DateTime.Now;
			state = hub.addTimeUp(ms, this);
		}

		public void Cancel(bool isThrow) {
			var hub = CothreadHub.Instance;
			if (hub.CurrentCothread.Timeout == this)
				hub.CurrentCothread.Timeout = null;
			if (isThrow && Timeout) 
				throw new CothreadTimeoutError(this);
			cancel = true;
		}

		public override string ToString() {
			return string.Format("Cothread.CothreadTimeout<{0}>(timeout={1}, state={2}", 
			                     GetHashCode(), state.ToString(), TimeoutTime);
		}

	}



}



