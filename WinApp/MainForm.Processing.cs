using System;

namespace JSAI.WinApp
{
    public partial class MainForm
    {
        private TaskProcessingForm? _activeProcessingForm;
        private int _processingFormDepth;

        private IDisposable BeginProcessingScope(string detail)
        {
            if (IsDisposed)
            {
                return ProcessingFormScope.Empty;
            }

            if (_activeProcessingForm == null || _activeProcessingForm.IsDisposed)
            {
                _activeProcessingForm = TaskProcessingForm.ShowFor(this, detail);
            }
            else
            {
                _activeProcessingForm.SetDetail(detail);
            }

            _processingFormDepth++;
            return new ProcessingFormScope(this);
        }

        private void SetProcessingDetail(string detail)
        {
            if (_activeProcessingForm == null || _activeProcessingForm.IsDisposed)
            {
                return;
            }

            _activeProcessingForm.SetDetail(detail);
        }

        private void EndProcessingScope()
        {
            if (_processingFormDepth > 0)
            {
                _processingFormDepth--;
            }

            if (_processingFormDepth != 0)
            {
                return;
            }

            _activeProcessingForm?.CloseSafely();
            _activeProcessingForm = null;
        }

        private sealed class ProcessingFormScope : IDisposable
        {
            public static readonly ProcessingFormScope Empty = new(null);

            private readonly MainForm? _owner;
            private bool _disposed;

            public ProcessingFormScope(MainForm? owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner?.EndProcessingScope();
            }
        }
    }
}
