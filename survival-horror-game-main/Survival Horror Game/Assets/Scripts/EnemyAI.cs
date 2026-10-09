using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Basit canavar yapay zekası: sürekli oyuncuya doğru yürür.
/// Oyuncu "fark etme mesafesi" (Detection Radius) içindeyse tam hızla kovalar,
/// dışındaysa yavaş yavaş yaklaşır. Hız ve mesafe PuzzleManager tarafından her notta artırılır.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EnemyAI : MonoBehaviour
{
    [Header("Hedef")]
    [SerializeField] private Transform target;

    [Header("Temel değerler (0 not toplanmışken)")]
    [SerializeField] private float baseChaseSpeed = 2.0f;
    [SerializeField] private float baseDetectionRadius = 8f;

    [Header("Davranış")]
    [Tooltip("Oyuncuyu fark etmediğinde, kovalama hızının bu katı kadar yavaş yaklaşır.")]
    [SerializeField] private float searchSpeedFactor = 0.35f;
    [Tooltip("Oyuncu fark etme mesafesinden çıksa da bu kadar saniye daha kovalamaya devam eder.")]
    [SerializeField] private float loseTargetDelay = 2.5f;
    [SerializeField] private float catchDistance = 1.3f;
    [Tooltip("Oyun başlayınca canavarın hareketsiz kalacağı süre (sn).")]
    [SerializeField] private float startDelay = 15f;

    public float CurrentChaseSpeed { get; private set; }
    public float CurrentDetectionRadius { get; private set; }
    public bool IsChasing { get; private set; }

    public event Action PlayerCaught;

    private NavMeshAgent _agent;
    private float _activeAt;
    private float _lastDetectedTime = -999f;
    private float _nextPathTime;
    private bool _caught;
    private bool _stopped;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        CurrentChaseSpeed = baseChaseSpeed;
        CurrentDetectionRadius = baseDetectionRadius;
        _activeAt = Time.time + startDelay;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    /// <summary>Hız ve fark etme mesafesini temel değerin üstüne ekler (kod üzerinden agresifleştirme).</summary>
    public void SetAggression(float extraSpeed, float extraDetectionRadius)
    {
        CurrentChaseSpeed = baseChaseSpeed + extraSpeed;
        CurrentDetectionRadius = baseDetectionRadius + extraDetectionRadius;
        Debug.Log("[Canavar] Hiz: " + CurrentChaseSpeed.ToString("0.0") +
                  " | Fark etme mesafesi: " + CurrentDetectionRadius.ToString("0.0"));
    }

    public void Halt()
    {
        _stopped = true;
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
    }

    private void Update()
    {
        if (_caught || _stopped || target == null || !_agent.isOnNavMesh) return;
        if (Time.time < _activeAt) return;

        Vector3 flat = target.position - transform.position;
        float heightDiff = Mathf.Abs(flat.y);
        flat.y = 0f;
        float distance = flat.magnitude;

        if (distance <= CurrentDetectionRadius) _lastDetectedTime = Time.time;
        IsChasing = Time.time - _lastDetectedTime <= loseTargetDelay;

        _agent.speed = IsChasing ? CurrentChaseSpeed : CurrentChaseSpeed * searchSpeedFactor;

        if (Time.time >= _nextPathTime)
        {
            _nextPathTime = Time.time + 0.25f;
            _agent.SetDestination(target.position);
        }

        if (distance <= catchDistance && heightDiff < 2f)
        {
            _caught = true;
            _agent.isStopped = true;
            if (PlayerCaught != null) PlayerCaught();
        }
    }
}
