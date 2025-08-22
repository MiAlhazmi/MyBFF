using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;

public class LuluAnimationManager : MonoBehaviour
{
    [FormerlySerializedAs("walkspeed")] [Header("Movement Settings")]
    public float walkSpeed = 2f;
    public float runSpeed = 3.5f;
    public float rotationSpeed = 180f;
    public float arrivalDistance = 0.5f;
    
    [Header("Timing Settings")]
    public float minIdleTime = 3f;
    public float maxIdleTime = 8f;
    public float moveChance = 0.3f; // 30% chance to move vs idle action
    public float runChance = 0.2f; // Chance to run instead of walk
    
    private Animator animator;
    private Transform[] waypoints;
    private Transform currentTarget;
    private bool isMoving = false;
    private float nextActionTime;
    private float currentMoveSpeed = 2f;
    private bool isInConversation = false;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        animator = GetComponent<Animator>();
        
        // Find waypoints
        GameObject[] waypointObjects = GameObject.FindGameObjectsWithTag("Waypoint");
        waypoints = new Transform[waypointObjects.Length];
        for (int i = 0; i < waypointObjects.Length; i++)
        {
            waypoints[i] = waypointObjects[i].transform;
        }
        
        ScheduleNextAction();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isInConversation)
        {
            if (isMoving)
            {
                MoveTowardsTarget();
            }
            else if (Time.time >= nextActionTime)
            {
                DecideNextAction();
            }
        }
    }


    void ScheduleNextAction()
    {
        float waitTime = Random.Range(minIdleTime, maxIdleTime);
        nextActionTime = Time.time + waitTime;
    }

    void DecideNextAction()
    {
        if (waypoints.Length > 0 && Random.value < moveChance)
        {
            StartMovingToRandomWaypoint();
        }
        else
        {
            DoRandomIdleAction();
            ScheduleNextAction();
        }
    }
    
    void StartMovingToRandomWaypoint()
    {
        if (waypoints.Length == 0) return;
        
        currentTarget = waypoints[Random.Range(0, waypoints.Length)];
        isMoving = true;
        
        // Choose walk or run randomly
        bool shouldRun = Random.value < runChance; // 20% chance to run
        currentMoveSpeed = shouldRun ? runSpeed : walkSpeed;
        
        animator.SetBool("IsWalking", !shouldRun);
        animator.SetBool("IsRunning", shouldRun);
        
        // Face the target
        StartCoroutine(RotateTowardsTarget());
    }
    
    IEnumerator RotateTowardsTarget()
    {
        Vector3 direction = (currentTarget.position - transform.position).normalized;
        direction.y = 0; // Keep on horizontal plane
        
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            
            while (Quaternion.Angle(transform.rotation, targetRotation) > 5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
                yield return null;
            }
        }
    }
    
    IEnumerator RotateToWaypointDirection()
    {
        Quaternion targetRotation = currentTarget.rotation;
    
        while (Quaternion.Angle(transform.rotation, targetRotation) > 5f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            yield return null;
        }
    }
    
    void MoveTowardsTarget()
    {
        if (currentTarget.IsUnityNull()) return;
        
        Vector3 direction = (currentTarget.position - transform.position).normalized;
        direction.y = 0; // Keep on horizontal plane
        
        // move towards target.
        transform.Translate(direction * currentMoveSpeed * Time.deltaTime, Space.World);
        
        // Check if arrived
        float distance = Vector3.Distance(transform.position, currentTarget.position);
        if (distance <= arrivalDistance)
        {
            Debug.Log("distance: " + distance);
            StopMoving();
        }
        
    }

    void StopMoving()
    {
        if (!currentTarget.IsUnityNull())
        {
            StartCoroutine(RotateToWaypointDirection());
        }
        
        isMoving = false;
        currentTarget = null;
        animator.SetBool("IsWalking", false);
        animator.SetBool("IsRunning", false);
        
        ScheduleNextAction();
    }

    
    void DoRandomIdleAction()
    {
        animator.SetInteger("IdleActionIndex", Random.Range(0, 5));
        animator.SetTrigger("DoIdleAction");
    }
    public void StopAllMovement()
    {
        if (isMoving) StopMoving();
        StopAllCoroutines();
    }
    
    // Public methods to control conversation state
    public void StartConversation()
    {
        isInConversation = true;
        StopAllMovement();
    }
    
    public void EndConversation()
    {
        isInConversation = false;
        ScheduleNextAction(); // Resume ambient behavior
    }
}
