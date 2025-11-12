using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class AIChaseAndWander : MonoBehaviour
{
    [Header("References")]
    public Transform player;

    [Header("AI Settings")]
    public float wanderRadius = 20f;
    public float wanderTimer = 5f;
    public float idleTimeAtDestination = 3f;
    public float maxDetectionDistance = 10f; // Maximum distance for line-of-sight check
    public float faceSpeed = 5f;

    [Header("Stop Distance")]
    public float stopThreshold = 0.15f; // How close to the player before we consider "arrived"
    public float attackRange = 2f; // Range within which AI can initiate attack

    [Header("Door Interaction")]
    public float doorDetectionRadius = 2f;
    public LayerMask doorLayer;

    [Header("Line of Sight")]
    public LayerMask obstructionLayer; // Layer for walls and masked objects

    private NavMeshAgent agent;
    private Animator animator;
    private float wanderCountdown;
    private bool isChasing = false;
    private bool isIdle = false;
    private bool isAttacking = false;
    private float idleTimer = 0f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        wanderCountdown = wanderTimer;
        SetNewRandomDestination();
    }

    void Update()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        bool canSeePlayer = false;

        // Check if player is within max detection distance and has line of sight
        if (distanceToPlayer <= maxDetectionDistance)
        {
            Vector3 directionToPlayer = (player.position - transform.position).normalized;
            Ray ray = new Ray(transform.position + Vector3.up * 1f, directionToPlayer);
            canSeePlayer = !Physics.Raycast(ray, distanceToPlayer, obstructionLayer);
        }

        // If player is behind an obstruction (wall or masked object), force AI to stop chasing/attacking and start wandering
        if (!canSeePlayer && (isChasing || isAttacking))
        {
            isChasing = false;
            isAttacking = false;
            isIdle = false;
            idleTimer = 0f;
            wanderCountdown = 0f;
            agent.isStopped = false;
            animator.SetFloat("Blend", 0f); // Reset animation to idle/walk
            SetNewRandomDestination();
        }

        if (canSeePlayer)
        {
            isChasing = true;
            isIdle = false;

            // Check if AI is currently attacking and animation is playing
            if (isAttacking)
            {
                // Only stop attacking if the attack animation is complete
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (!stateInfo.IsName("Attack") || stateInfo.normalizedTime >= 1f)
                {
                    isAttacking = false;
                }
                else
                {
                    // Continue attacking: keep agent stopped and face player
                    agent.isStopped = true;
                    animator.SetFloat("Blend", 2f); // Maintain attack animation
                    FacePlayerSmoothly();
                }
            }

            // Only process chase/attack logic if not currently locked in attack animation
            if (!isAttacking)
            {
                // Chase the player
                agent.isStopped = false;
                agent.SetDestination(player.position);

                // Check if AI is close enough to start attacking
                float distToTarget = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z), 
                                                    new Vector3(player.position.x, 0f, player.position.z));
                if (distToTarget <= stopThreshold)
                {
                    // Reached the player -> stop and attack
                    agent.isStopped = true;
                    isAttacking = true;
                    animator.SetFloat("Blend", 2f);
                    FacePlayerSmoothly();
                }
                else
                {
                    // Moving toward player: rotateORDIN naturally toward movement direction
                    if (agent.velocity.sqrMagnitude > 0.01f)
                    {
                        Vector3 lookDir = agent.velocity.normalized;
                        lookDir.y = 0f;
                        if (lookDir != Vector3.zero)
                        {
                            Quaternion targetRot = Quaternion.LookRotation(lookDir);
                            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * faceSpeed);
                        }
                    }
                }
            }
        }
        else if (!isChasing && !isAttacking)
        {
            // Wandering / idle logic
            if (!isIdle)
            {
                wanderCountdown += Time.deltaTime;
                if ((agent.remainingDistance <= agent.stoppingDistance && !agent.pathPending) || wanderCountdown >= wanderTimer)
                {
                    StartIdleState();
                }
            }
            else
            {
                idleTimer += Time.deltaTime;
                if (idleTimer >= idleTimeAtDestination)
                {
                    isIdle = false;
                    idleTimer = 0f;
                    wanderCountdown = 0f;
                    SetNewRandomDestination();
                }
            }
        }

        // Animation blend based on state
        if (!isAttacking)
        {
            float speedPercent = agent.velocity.magnitude / Mathf.Max(agent.speed, 0.0001f);
            animator.SetFloat("Blend", speedPercent);
        }

        // Door checks
        CheckForDoors();
    }

    void FacePlayerSmoothly()
    {
        Vector3 dir = (player.position - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(dir.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * faceSpeed);
        }
    }

    void SetNewRandomDestination()
    {
        agent.isStopped = false;
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
        }
    }

    void StartIdleState()
    {
        isIdle = true;
        agent.isStopped = true;
        idleTimer = 0f;
        animator.SetFloat("Blend", 0f);
    }

    void CheckForDoors()
    {
        Collider[] doors = Physics.OverlapSphere(transform.position, doorDetectionRadius, doorLayer);
        foreach (Collider door in doors)
        {
            var doorOpener = door.GetComponent<DoorOpener>();
            if (doorOpener != null)
            {
                bool isOpen = (bool)typeof(DoorOpener).GetField("isOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(doorOpener);
                bool isMoving = (bool)typeof(DoorOpener).GetField("isMoving", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(doorOpener);
                if (!isOpen && !isMoving)
                {
                    typeof(DoorOpener).GetField("isOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(doorOpener, true);
                    typeof(DoorOpener).GetField("isMoving", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(doorOpener, true);
                    Debug.Log($"AI opened door: {door.gameObject.name}");
                }
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, maxDetectionDistance);
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, doorDetectionRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}