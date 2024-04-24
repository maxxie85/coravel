using System;
using System.Threading.Tasks;

namespace Coravel.Tasks
{
    public class ActionOrAsyncFunc
    {
        private readonly Action _action;
        private readonly Func<Task> _asyncAction;
        private readonly bool _isAsync;

        public ActionOrAsyncFunc(Action action)
        {
            _isAsync = false;
            _action = action;
            Guid = Guid.NewGuid();
        }

        public ActionOrAsyncFunc(Func<Task> asyncAction)
        {
            _isAsync = true;
            _asyncAction = asyncAction;
            Guid = Guid.NewGuid();
        }

        public Guid Guid { get; }

        public async Task Invoke()
        {
            if (_isAsync)
            {
                await _asyncAction();
            }
            else
            {
                _action();
            }
        }
    }
}