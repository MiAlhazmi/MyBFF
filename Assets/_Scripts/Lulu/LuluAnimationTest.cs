using UnityEngine;

public class LuluAnimationTest : MonoBehaviour
{
    private Animator animator;
    
    void Start()
    {
        animator = GetComponent<Animator>();
        InvokeRepeating("DoRandomIdleAction", 3f, 5f);
    }
    
    void DoRandomIdleAction()
    {
        animator.SetInteger("IdleActionIndex", Random.Range(0, 5));
        animator.SetTrigger("DoIdleAction");
    }
}
