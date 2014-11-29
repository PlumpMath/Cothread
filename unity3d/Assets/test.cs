using UnityEngine;
using System.Collections;
using Cothreads;

public class test : MonoBehaviour {

	// Use this for initialization
	void Start () {
        UnityHub.Install(this);
        UnityHub.Instance.StartCothread(Runtest(), this.gameObject);
	}

    IEnumerator Runtest()
    {
        UnityHub.test(1);
        yield return null;
    }
	
	// Update is called once per frame
	void Update () {
	
	}
}
