using UnityEngine;


namespace MyBFF.Voice
{
    [RequireComponent(typeof(Collider))]
    public class ELConversationZone : MonoBehaviour
    {
        public string playerTag = "Player";
        public bool autoStartOnEnter = true;
        public bool autoStopOnExit = true;

        void Reset()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (autoStartOnEnter) ConversationManager.Instance?.BeginConversation();
        }

        void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag(playerTag)) return;
            if (autoStopOnExit) ConversationManager.Instance?.EndConversation();
        }
    }
}
