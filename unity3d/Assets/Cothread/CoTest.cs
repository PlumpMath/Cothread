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
using System.Threading;
using System.Diagnostics;
using UnityEngine;

using AssertDebug = System.Diagnostics.Debug;

namespace Cothreads {
	public class CothreadTestCase {
		public UnityHub hub;
		public void Start() {
			hub = UnityHub.Install(null);
		}
		
		public void test(int count) {
			hub.StartCoroutine(testThread());
			hub.StartCoroutine(testTimeout1());
			hub.StartCoroutine(testTimeout2());
			testEvent1(count);
			hub.StartCoroutine(testEvent2());
			hub.StartCoroutine(testCothread());
			hub.StartCoroutine(testJoin());

			testSock(count);

			hub.StartCoroutine(testU3d(count));
			hub.StartCoroutine(testU3dStartCothread());
		}
		
		IEnumerator testTimeout1() {
                	var timeout = CothreadTimeout.NewWithStart(1005);
			yield return hub.Sleep(1000);
			try {
				timeout.Cancel(true);
				CothreadHub.Log("[testTimeout1] ok");
			} catch ( CothreadTimeoutError ) {
				CothreadHub.Log("[testTimeout1] error");
			}
		}
		IEnumerator testTimeout2() {
			var timeout = CothreadTimeout.NewWithStart(1005);
			yield return hub.Sleep(1010);
			try {
				timeout.Cancel(true);
				CothreadHub.Log("[testTimeout2] CothreadTimeoutError error");
			} catch ( CothreadTimeoutError ) {
				CothreadHub.Log("[testTimeout2] CothreadTimeoutError ok");
			}
		}
		
		void testEvent1(int count) {
			CothreadEvent ev, nev, bev;
			bev = ev = new CothreadEvent();
			for (int i = 0; i < count; i++) {
				if (i+1 == count) {
					nev = null;
				} else {
					nev = new CothreadEvent();
				}
				hub.StartCoroutine(_testEvent1(i+1, ev, nev));
				ev = nev;
			}
			bev.Set("event");
		}
		IEnumerator _testEvent1(int i, CothreadEvent ev, CothreadEvent nev) {
			yield return ev.Wait(0);
			var result = (string)ev.Get("");
			var msg = string.Format("{0} {1} ->", result, i.ToString());
			if (nev != null) {
				nev.Set(msg);
			} else {
				CothreadHub.Log("result: " + msg);
			}
		}

		IEnumerator testEvent2() {
			var ev = new CothreadEvent();
			hub.StartCoroutine(_testEvent2(ev));
			yield return ev.Wait();
			if (!ev.Get().Equals(1))
				CothreadHub.Log("[testEvent2] error");
			else
				CothreadHub.Log("[testEvent2] ok");
			ev.Clear();
			hub.StartCoroutine(_testEvent2(ev));
			yield return ev;
			AssertDebug.Assert(ev.Get().Equals(1), "[testEvent2] yield event error!");
		}
		IEnumerator _testEvent2(CothreadEvent ev) {
			yield return hub.Sleep(1000);
			ev.Set(1);
		}

		IEnumerator testCothread() {
			var ev = new CothreadEvent();
			yield return hub.StartCoroutine(_testEvent2(ev));
			if (ev.Get().Equals(1)) CothreadHub.Log("[testCothread] ok");
			else CothreadHub.Log("[testCothread] error");
		}

		IEnumerator testThread() {
			var sw = new Stopwatch();
			sw.Start();
			List<int> rs = new List<int>();
			var c = 1000;
			yield return hub.StartThread(delegate {
				Thread.Sleep(2000);
				for (int i=0; i < c; i++) rs.Add(i);
			});
			sw.Stop();
			var t = sw.ElapsedMilliseconds;
			if (rs.Count == c) CothreadHub.Log("[testThread] OK! pass time:" + t.ToString());
			else CothreadHub.Log("[testThread]ERROR! pass time:" + t.ToString());
		}




		//socket
		void testSock(int count) {
			for (int i=0; i<count; i++) {
				hub.StartCoroutine(testSock1(i));
			}
		}

		IEnumerator testSock1(int i) {
			string stri = i.ToString();
			CothreadHub.Log("testSock:" + stri);
			CothreadTimeout timeout = CothreadTimeout.NewWithStart(5);
			CothreadSocket sock = new CothreadSocket();
			yield return sock.Connect("192.168.0.210", 81);  //web server
			//yield return sock.Connect("www.baidu.com", 80);  //www.baidu.com
			//yield return sock.Connect("115.239.210.27", 80);  //www.baidu.com
			//yield return hub.Sleep(rt);
			//CothreadHub.Log(stri + "-socket connected:" + sock.Connected);
			
			yield return sock.SendString("GET / HTTP/1.0\n\n");
			//CothreadHub.Log(stri + "-Send ok");
			var recvData = new CothreadSocketRecvData(2048);
			var result = new CothreadResult<string>();

			yield return hub.Sleep(1000);
			yield return sock.RecvString(recvData, result);
			string s1 = (string)result.Result;
			if (string.IsNullOrEmpty(s1)) 
				CothreadHub.Log("testSock(" + stri +") error" + "-passTime:" + timeout.PassTime.ToString());
			else
				CothreadHub.Log("testSock(" + stri +") ok:" + s1.Length.ToString()  + "  data:" + s1.Substring(0, Math.Min(500, s1.Length)));
			try {
				timeout.Cancel(true);
			} catch (CothreadTimeoutError){
				CothreadHub.Log(stri + "-testSock timeout");
			} finally {
				sock.Close();
			}
		}


		IEnumerator testJoin() {
			var timeout = CothreadTimeout.NewWithStart(1);
			var ct1 = hub.StartCoroutine(_testJoin());
			yield return ct1.Join();
			if (!timeout.Timeout)
				CothreadHub.Log("[testJoin] error");
			else
				CothreadHub.Log("[testJoin] ok");
		}

		IEnumerator _testJoin() {
			yield return hub.Sleep(1000);
		}

		//u3d
		IEnumerator testU3d(int count) {
			var w1 = new WWW("http://www.baidu.com");
			yield return w1;
			if (!w1.isDone)
				CothreadHub.Log("[testU3d] www error");
			else
				CothreadHub.Log("[testU3d] www ok: size:" + w1.text.Length.ToString());

			var result = new CothreadResult<bool>();
			var u1 = UnityHub.Active.StartCoroutine(_u3dCoroutine1(result));
			yield return u1;
			if (result.Result.Equals(true))
				CothreadHub.Log("[testU3d]StartCoroutine ok");
			else
				CothreadHub.Log("[testU3d]StartCoroutine error");
		}

		IEnumerator _u3dCoroutine1(CothreadResult<bool> result) {
			yield return new WaitForSeconds(1);
			result.Result = true;
		}

		IEnumerator testU3dStartCothread() {
			var ev = new CothreadEvent();
			var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
			var th = hub.StartCothread(_testEvent2(ev), obj);
			yield return hub.Sleep(10);
			GameObject.Destroy(obj);
			yield return hub.Sleep(100);
			if (th.Closed && ev.Get()==null) CothreadHub.Log("[testU3dStartCothread] ok");
			else CothreadHub.Log("[testU3dStartCothread] error");
		}

	}

}








//////////////////////////////
//////////////////////////////















