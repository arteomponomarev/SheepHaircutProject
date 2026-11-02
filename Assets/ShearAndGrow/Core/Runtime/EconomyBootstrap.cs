using UnityEngine;

namespace ShearAndGrow.Economy
{
    /// <summary>Present in every scene: initialises the economy once with the shipped balance data.</summary>
    [DefaultExecutionOrder(-200)]
    public sealed class EconomyBootstrap : MonoBehaviour
    {
        [SerializeField] private GameBalanceData _balance;

        public GameBalanceData Balance => _balance;

        void Awake()
        {
            if (_balance == null) { Debug.LogError("[Economy] EconomyBootstrap has no GameBalanceData."); return; }
            WoolEconomy.Initialise(_balance);
        }
    }
}
