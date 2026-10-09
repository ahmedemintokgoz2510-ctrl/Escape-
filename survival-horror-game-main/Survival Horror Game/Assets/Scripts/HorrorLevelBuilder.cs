using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Oyun açılınca (sahne düzenlemesi gerektirmeden):
///  1) çalışma zamanında NavMesh üretir,
///  2) haritanın uzak köşelerine 3 şifre notu yerleştirir,
///  3) canavarı oyuncudan uzak bir noktaya koyar,
///  4) PuzzleManager'ı kurar.
/// </summary>
public class HorrorLevelBuilder : MonoBehaviour
{
    private static readonly string[] NoteTexts =
    {
        "Not: Şifrenin ilk rakamı 4. Bu evde yalnız değilsin...",
        "Not: İkinci rakam 7. Duvarlar dinliyor, sessiz ol.",
        "Not: Son rakam 2. Çıkış kapısı için şifre: 4-7-2."
    };

    [SerializeField] private int maxCandidates = 300;
    [SerializeField] private float minEnemyDistanceFromNotes = 8f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (FindFirstObjectByType<FirstPersonController>() == null) return;
        if (FindFirstObjectByType<HorrorLevelBuilder>() != null) return;
        new GameObject("Horror Level Builder").AddComponent<HorrorLevelBuilder>();
    }

    private IEnumerator Start()
    {
        yield return null; // diğer bileşenlerin Start'ı çalışsın
        yield return null;

        FirstPersonController player = FindFirstObjectByType<FirstPersonController>();
        if (player == null) yield break;

        BuildNavMesh(player);

        var puzzle = new GameObject("Puzzle Manager").AddComponent<PuzzleManager>();

        NavMeshHit startHit;
        if (!NavMesh.SamplePosition(player.transform.position, out startHit, 5f, NavMesh.AllAreas))
        {
            Debug.LogError("[Bulmaca] Oyuncunun yakininda NavMesh bulunamadi; not ve canavar yerlestirilemedi.");
            yield break;
        }

        List<Vector3> reachable = CollectReachablePoints(startHit.position, player.transform.position.y);
        if (reachable.Count < 5)
        {
            Debug.LogError("[Bulmaca] Ulasilabilir yeterli nokta bulunamadi (" + reachable.Count + ").");
            yield break;
        }

        // Notlar: oyuncudan ve kapıdan en uzak, birbirinden de en uzak noktalar (haritanın köşeleri).
        var anchors = new List<Vector3> { startHit.position };
        MetalDoor door = FindFirstObjectByType<MetalDoor>();
        NavMeshHit doorHit;
        if (door != null && NavMesh.SamplePosition(door.transform.position, out doorHit, 15f, NavMesh.AllAreas))
            anchors.Add(doorHit.position);

        var notePoints = new List<Vector3>();
        for (int i = 0; i < PuzzleManager.TotalNotes; i++)
        {
            Vector3 p = FarthestFrom(reachable, anchors);
            notePoints.Add(p);
            anchors.Add(p);
        }

        for (int i = 0; i < notePoints.Count; i++)
            SpawnNote(notePoints[i], NoteTexts[i % NoteTexts.Length], i + 1);

        SpawnEnemy(player.transform, startHit.position, reachable, notePoints, puzzle);
    }

    // ---------------------------------------------------------------- NavMesh

    private void BuildNavMesh(FirstPersonController player)
    {
        // Oyuncunun kendi gövdesi zemine "delik" açmasın diye derleme sırasında gizle.
        Renderer[] playerRenderers = player.GetComponentsInChildren<Renderer>();
        var wasEnabled = new bool[playerRenderers.Length];
        for (int i = 0; i < playerRenderers.Length; i++)
        {
            wasEnabled[i] = playerRenderers[i].enabled;
            playerRenderers[i].enabled = false;
        }

        var surfaceGo = new GameObject("Runtime NavMesh");
        var surface = surfaceGo.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.BuildNavMesh();

        for (int i = 0; i < playerRenderers.Length; i++)
            if (playerRenderers[i] != null) playerRenderers[i].enabled = wasEnabled[i];
    }

    private List<Vector3> CollectReachablePoints(Vector3 start, float playerY)
    {
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        var candidates = new List<Vector3>();

        // Sadece oyuncunun bulunduğu kat seviyesindeki noktalar (tavan/üst yüzeyler elenir).
        foreach (Vector3 v in tri.vertices)
        {
            if (v.y > playerY - 1.5f && v.y < playerY + 2.5f) candidates.Add(v);
        }

        // Rastgele karıştırıp sınırla (tek karede çok fazla yol hesabı yapmamak için).
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            Vector3 tmp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = tmp;
        }
        if (candidates.Count > maxCandidates) candidates.RemoveRange(maxCandidates, candidates.Count - maxCandidates);

        var reachable = new List<Vector3>();
        var path = new NavMeshPath();
        foreach (Vector3 c in candidates)
        {
            if (NavMesh.CalculatePath(start, c, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                reachable.Add(c);
        }
        return reachable;
    }

    private static Vector3 FarthestFrom(List<Vector3> points, List<Vector3> anchors)
    {
        Vector3 best = points[0];
        float bestScore = -1f;
        foreach (Vector3 p in points)
        {
            float minDist = float.MaxValue;
            foreach (Vector3 a in anchors)
                minDist = Mathf.Min(minDist, Vector3.Distance(p, a));
            if (minDist > bestScore) { bestScore = minDist; best = p; }
        }
        return best;
    }

    // ------------------------------------------------------------------ Not

    private void SpawnNote(Vector3 floorPoint, string text, int index)
    {
        var root = new GameObject("Sifre Notu " + index);
        root.transform.position = floorPoint;

        // Oyuncu kamerayı çeviremediği için not göz hizasında durur: ince bir direk + üstünde kâğıt.
        GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "Direk";
        post.transform.SetParent(root.transform, false);
        post.transform.localScale = new Vector3(0.12f, 0.65f, 0.12f);
        post.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        post.GetComponent<Renderer>().material.color = new Color(0.12f, 0.1f, 0.08f);
        Destroy(post.GetComponent<Collider>());

        GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        paper.name = "Kagit";
        paper.transform.SetParent(root.transform, false);
        paper.transform.localScale = new Vector3(0.34f, 0.04f, 0.44f);
        paper.transform.localPosition = new Vector3(0f, 1.32f, 0f);
        paper.transform.localRotation = Quaternion.Euler(-70f, Random.Range(0f, 360f), 0f);
        paper.GetComponent<Renderer>().material.color = new Color(0.95f, 0.92f, 0.78f);
        Destroy(paper.GetComponent<Collider>());

        var lightGo = new GameObject("Not Isigi");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 3.5f;
        light.intensity = 1.5f;
        light.color = new Color(1f, 0.85f, 0.55f);
        light.shadows = LightShadows.None;

        // Göz hizasındaki nişan ışınının çarpacağı etkileşim kutusu.
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.85f, 0f);
        box.size = new Vector3(0.6f, 1.7f, 0.6f);

        var note = root.AddComponent<CodeNote>();
        note.noteText = text;
    }

    // --------------------------------------------------------------- Canavar

    private void SpawnEnemy(Transform player, Vector3 playerStart, List<Vector3> reachable,
                            List<Vector3> notePoints, PuzzleManager puzzle)
    {
        Vector3 spawn = reachable[0];
        float bestDist = -1f;
        foreach (Vector3 p in reachable)
        {
            bool farFromNotes = true;
            foreach (Vector3 n in notePoints)
                if (Vector3.Distance(p, n) < minEnemyDistanceFromNotes) { farFromNotes = false; break; }
            if (!farFromNotes) continue;

            float d = Vector3.Distance(p, playerStart);
            if (d > bestDist) { bestDist = d; spawn = p; }
        }

        var root = new GameObject("Canavar");
        root.transform.position = spawn;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Govde";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        body.transform.localScale = new Vector3(0.8f, 0.95f, 0.8f);
        body.GetComponent<Renderer>().material.color = new Color(0.3f, 0.02f, 0.02f);
        Destroy(body.GetComponent<Collider>());

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.4f;
        agent.height = 1.9f;
        agent.acceleration = 10f;
        agent.angularSpeed = 300f;
        agent.stoppingDistance = 0.3f;
        agent.autoBraking = false;
        agent.Warp(spawn);

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.95f, 0f);
        capsule.radius = 0.4f;
        capsule.height = 1.9f;

        var enemy = root.AddComponent<EnemyAI>();
        enemy.SetTarget(player);
        puzzle.RegisterEnemy(enemy);
    }
}
