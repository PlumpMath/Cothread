#region Header
/**
 *  Cothread for unity3d
 *  Author: seewind
 *  https://github.com/seewindcn/unity-async
 *  email: seewindcn@gmail.com
 **/
#endregion

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

using Cothreads;

public class CoHub: MonoBehaviour {
	public UnityHub Hub { get { return (UnityHub)UnityHub.Instance; } }

    public void Start() {
    	UnityHub.Install(this);
    }
}

public class UnityHub: CothreadHub {
	public static MonoBehaviour Active;
	public new static UnityHub Instance {
		get { return (UnityHub)CothreadHub.Instance; }
	}

	public static UnityHub Install(MonoBehaviour active) {
		if (Instance != null) return Instance;
		Active = active;
		var Hub = new UnityHub();
		CothreadHub.Instance = Hub;
		Hub.Start();
		return Hub;
	}

	public new void Start() {
		if (!Stoped)
			throw new SystemException("startLoop");
		base.Start();
		if (CothreadHub.LogHandler == null)
			CothreadHub.LogHandler = Debug.Log;
		BusyTickTime = 0.0005f;
		IdleTickTime = 0.005f;
		registerU3d();
		Active.StartCoroutine(loop());
	}

	IEnumerator loop() {
		double sleepTime;
		while (!Stoped) {
			sleepTime = Tick();
			if (sleepTime > BusyTickTime) yield return new WaitForSeconds((float)sleepTime);
			else yield return null;
		}
	}

	public new void Stop() {
		if (Stoped)
			return;
		base.Stop();
		unRegisterU3d();
	}

	public static void test(int count) {
		var tc = new CothreadTestCase();
		tc.Start();
		tc.test(count);
	}


	#region 扩展支持u3d

	public Cothread StartCothread(IEnumerator routine, GameObject owner) {
		CothreadsBehaviour cb = owner.GetComponent<CothreadsBehaviour>();
		if (cb == null) cb = owner.AddComponent<CothreadsBehaviour>();
		Cothread th = StartCoroutine(routine);
		cb.AddCothread(th);
		return th;
	}


	public IEnumerator SleepWithTimeScale(uint ms) {
		return Sleep((uint)((float)ms / Time.timeScale));
	}

	void registerU3d() {
		CothreadHub.GlobalAsyncHandle = u3dAsyncCheck;
	}

	void unRegisterU3d() {
		CothreadHub.GlobalAsyncHandle = null;
	}


	object u3dAsyncCheck(IEnumerator ie) {
		if (ie.Current == null)
		{
			return null;
		}
		if (ie.Current.GetType().IsSubclassOf(typeof(YieldInstruction)) 
		    || ie.Current is WWW 
		    || ie.Current is WWWForm
		    //|| ie.Current is Coroutine
		    ) {
			return u3dAsyncHandle(ie);
		}
		return null;
	}

	IEnumerable u3dAsyncHandle(IEnumerator ie) {
		addCallback(ie);
		Active.StartCoroutine(_u3dAsyncHandle(current, ie));
		yield return CothreadHub.YIELD_CALLBACK;
	}

	IEnumerator _u3dAsyncHandle(IEnumerator curIE, IEnumerator ie) {
		var cur = ie.Current;
		yield return cur;
		if (ie.Current == cur)
			addCothread(curIE);
	}

	#endregion

}

public static class UnityExtensions {
	public static Cothread StartCothread(this MonoBehaviour target, IEnumerator routine) {
		return UnityHub.Instance.StartCothread(routine, target.gameObject);
	}
}

class CothreadsBehaviour : MonoBehaviour {
	private HashSet<Cothread> threads = new HashSet<Cothread>();
	private bool destroying;
	public void AddCothread(Cothread thread) {
		threads.Add(thread);
		thread.ev.onSet += onClose;
	}

	void onClose(object thread) {
		var th = (Cothread)thread;
		th.ev.onSet -= onClose;
		if (!destroying) threads.Remove(th);
	}

	void OnDestroy() {
		destroying = true;
		foreach (var th in threads) {
			th.Close();
		}
		threads.Clear();
	}
}

