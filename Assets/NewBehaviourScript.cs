using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    [Header("CPU Spike Settings")]
    [SerializeField] float spikeChance = 0.05f;
    [SerializeField] int spikeIterations = 500000;

    [Header("GC Pressure Settings")]
    [SerializeField] float gcChance = 0.08f;
    [SerializeField] int gcAllocKB = 64;

    [Header("Visual")]
    [SerializeField] int cubeCount = 20;

    readonly List<GameObject> cubes = new List<GameObject>();
    readonly List<byte[]> gcJunk = new List<byte[]>();
    float hueOffset;
    int frameCount;

    void Start()
    {
        Application.targetFrameRate = 30;

        // Spawn cubes in a circle
        for (int i = 0; i < cubeCount; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.localScale = Vector3.one * 0.5f;
            cube.GetComponent<Renderer>().material = new Material(Shader.Find("Standard"));
            cubes.Add(cube);
        }
    }

    void Update()
    {
        frameCount++;
        hueOffset += Time.deltaTime * 0.1f;

        // Animate cubes — visual change every frame for screenshot testing
        for (int i = 0; i < cubes.Count; i++)
        {
            float angle = (i / (float)cubes.Count) * Mathf.PI * 2f + Time.time * 0.5f;
            float radius = 3f + Mathf.Sin(Time.time * 0.3f + i) * 1.5f;
            cubes[i].transform.position = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(Time.time * 2f + i * 0.5f) * 1.5f,
                Mathf.Sin(angle) * radius);

            cubes[i].transform.Rotate(Vector3.up * (60f + i * 10f) * Time.deltaTime);

            // Cycle colors so each frame looks different
            float hue = (i / (float)cubes.Count + hueOffset) % 1f;
            cubes[i].GetComponent<Renderer>().material.color = Color.HSVToRGB(hue, 0.8f, 1f);
        }

        // Random CPU spike
        if (Random.value < spikeChance)
        {
            float dummy = 0f;
            for (int i = 0; i < spikeIterations; i++)
                dummy += Mathf.Sqrt(i * 0.001f);
            // Prevent optimization
            if (dummy < -1f) Debug.Log(dummy);
        }

        // Random GC pressure
        if (Random.value < gcChance)
        {
            gcJunk.Add(new byte[gcAllocKB * 1024]);
            // Keep list from growing forever
            if (gcJunk.Count > 20)
                gcJunk.RemoveAt(0);
        }
    }
}
