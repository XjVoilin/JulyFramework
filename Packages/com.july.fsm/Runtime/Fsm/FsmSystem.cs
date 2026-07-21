using System.Collections.Generic;
using July.Arch;

namespace July.Fsm
{
    public class FsmSystem : SystemBase, IFsmSystem
    {
        private readonly HashSet<IFsm> _fsms = new();

        protected override void OnShutdown()
        {
            DestroyAllFsms();
        }

        public IFsm CreateFsm(object owner, Dictionary<int, IFsmState> states, int defaultState)
        {
            var fsm = new FsmInstance(owner, states, defaultState);
            _fsms.Add(fsm);
            return fsm;
        }

        public void DestroyFsm(IFsm fsm)
        {
            if (fsm != null)
                _fsms.Remove(fsm);
        }

        public void DestroyAllFsms()
        {
            _fsms.Clear();
        }

        private sealed class FsmInstance : IFsm
        {
            private readonly object _owner;
            private readonly Dictionary<int, IFsmState> _states;
            private int _currentStateId;
            private int _previousStateId;
            private IFsmState _currentInstance;
            private readonly List<int> _history = new();
            private const int MaxHistorySize = 10;

            public object Owner => _owner;
            public int CurrentState => _currentStateId;
            public int PreviousState => _previousStateId;
            public IReadOnlyList<int> StateHistory => _history;

            public FsmInstance(object owner, Dictionary<int, IFsmState> states, int defaultState)
            {
                _owner = owner;
                _states = states;
                _currentStateId = defaultState;
                _previousStateId = defaultState;

                foreach (var state in _states.Values)
                    state.OnInit(this);

                if (_states.TryGetValue(defaultState, out _currentInstance))
                {
                    _history.Add(defaultState);
                    _currentInstance.OnEnter();
                }
            }

            public bool ChangeState(int newState)
            {
                if (newState == _currentStateId) return false;
                if (!_states.TryGetValue(newState, out var newInstance)) return false;
                if (_currentInstance != null && !_currentInstance.CanChangeTo(newState)) return false;

                _currentInstance?.OnExit();

                _previousStateId = _currentStateId;
                _currentStateId = newState;
                _currentInstance = newInstance;

                if (_history.Count >= MaxHistorySize)
                    _history.RemoveAt(0);
                _history.Add(newState);

                _currentInstance.OnEnter();
                return true;
            }

            public void Update(float elapseSeconds, float realElapseSeconds)
            {
                _currentInstance?.OnUpdate();
            }

            public bool CanChangeTo(int state)
            {
                if (!_states.ContainsKey(state)) return false;
                return _currentInstance?.CanChangeTo(state) ?? true;
            }
        }
    }
}
