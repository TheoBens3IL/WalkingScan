using UnityEngine;

namespace WalkingScan
{
    /// <summary>
    /// Example script for the WalkingScan Unity project.
    /// This demonstrates the basic structure and namespace organization.
    /// </summary>
    public class ExampleScript : MonoBehaviour
    {
        [SerializeField]
        private string projectName = "WalkingScan";
        
        [SerializeField]
        private bool debugMode = true;

        private void Start()
        {
            if (debugMode)
            {
                Debug.Log($"Welcome to {projectName}! Project initialized successfully.");
            }
        }

        private void Update()
        {
            // Example update logic can be added here
        }
    }
}