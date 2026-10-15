using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ReTray_App.Controls;

/// <summary>A regular WPF HWND with the LAN tool's persistent Acrylic and custom chrome.</summary>
public class MaterialWindow : Window
{
    private const string TintResource = "WindowTintBrush";
    private readonly TaskCompletionSource firstReveal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private HwndSource? source;
    private NativeWindowFrame? frame;
    private DispatcherOperation? revealOperation;
    private DispatcherOperation? transitionOperation, backdropOperation;
    private readonly WindowTransitionBackdrop transitionBackdrop;
    private bool minimizePending, backdropQueued;
    public System.Windows.Controls.Panel? TransitionRoot { get; set; }
    private bool dark, rendered, revealed, closed;

    public MaterialWindow()
    {
        transitionBackdrop = new WindowTransitionBackdrop(this);
        Width = 440; Height = 720; MinWidth = 300; MinHeight = 260;
        // Match the LAN tool's ordinary Window + WindowChrome configuration.
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        AllowsTransparency = false;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        if (TryFindResource(TintResource) is SolidColorBrush tint)
            dark = tint.Color.R + tint.Color.G + tint.Color.B < 384;
        // Both WPF background layers stay fully transparent, as in the LAN tool.
        Background = Brushes.Transparent;

        // The 32-DIP top strip uses normal native caption dragging. Mark caption
        // buttons with WindowChrome.IsHitTestVisibleInChrome=True in the UI.
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 32,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            UseAeroCaptionButtons = false
        });
        CommandBindings.Add(new System.Windows.Input.CommandBinding(SystemCommands.MaximizeWindowCommand,
            (_, args) => args.Handled = true,
            (_, args) => { args.CanExecute = false; args.Handled = true; }));
        ContentRendered += FirstContentRendered;
        Loaded += WindowLoaded;
        Closed += WindowClosed;
        StateChanged += TransitionStateChanged;
    }

    /// <summary>Updates the existing HWND after application theme resources change.</summary>
    public void ApplyTheme(bool dark)
    {
        VerifyAccess();
        this.dark = dark;
        if(transitionBackdrop.IsPrepared)return;
        ApplyNativeAppearance(refreshFrame:true);
    }

    public void RequestMinimizeWithTransitionMaterial()
    {
        VerifyAccess();
        if(closed||minimizePending||WindowState==WindowState.Minimized)return;
        minimizePending=true;
        transitionOperation=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            transitionOperation=null;
            if(closed||!minimizePending||WindowState==WindowState.Minimized)return;
            try
            {
                transitionBackdrop.Prepare(dark,TransitionRoot);
                if(!closed&&minimizePending)WindowState=WindowState.Minimized;
                if(WindowState!=WindowState.Minimized)CompleteWindowTransition();
            }
            catch{CompleteWindowTransition();throw;}
        }));
        (TransitionRoot??Content as System.Windows.Controls.Panel)?.InvalidateVisual();
    }

    // Synchronous: the caller may shut down immediately after this returns.
    public void PrepareCloseTransition()
    {
        VerifyAccess();
        transitionOperation?.Abort();transitionOperation=null;minimizePending=false;
        transitionBackdrop.Prepare(dark,TransitionRoot);
    }

    public void HidePrepared()
    {
        PrepareCloseTransition();
        Hide();
    }

    public void CompleteWindowTransition()
    {
        VerifyAccess();
        transitionOperation?.Abort();transitionOperation=null;
        transitionBackdrop.Complete();minimizePending=false;
        QueueBackdropRefresh();
    }

    private void TransitionStateChanged(object? sender,EventArgs args)
    {
        if(WindowState!=WindowState.Minimized&&transitionBackdrop.IsPrepared)CompleteWindowTransition();
        else QueueBackdropRefresh();
    }

    private void QueueBackdropRefresh()
    {
        if(backdropQueued||closed)return;
        backdropQueued=true;
        backdropOperation=Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
        {
            backdropQueued=false;backdropOperation=null;
            if(!closed&&IsVisible&&WindowState!=WindowState.Minimized&&!transitionBackdrop.IsPrepared)
                ApplyNativeAppearance(refreshFrame:true);
        }));
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs args)
    {
        base.OnClosing(args);
        if(args.Cancel)
        {
            if(transitionBackdrop.IsPrepared)CompleteWindowTransition();
            return;
        }
        // Includes editor/confirmation windows after their own save/cancel
        // handlers accept closing. Main shutdown may already have prepared it.
        if(!closed&&!transitionBackdrop.IsPrepared)PrepareCloseTransition();
    }

    /// <summary>Prepares the hidden HWND and shows the complete first rendered scene.</summary>
    public void ShowPrepared()
    {
        VerifyAccess();
        if (closed) throw new InvalidOperationException("A closed material window cannot be shown again.");
        new WindowInteropHelper(this).EnsureHandle();
        if (!IsVisible)
        {
            if(transitionBackdrop.IsPrepared)CompleteWindowTransition();
            Show();
            if(revealed)ApplyNativeAppearance(refreshFrame:true);
        }
        if (rendered && !revealed) QueueReveal();
    }

    /// <summary>Shows the window and completes after its first native reveal.</summary>
    public Task RevealAsync()
    {
        ShowPrepared();
        return firstReveal.Task;
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        if (source == null) throw new InvalidOperationException("The WPF window source was not initialized.");
        frame = new NativeWindowFrame(source.Handle);
        frame.PrepareFirstShow();
        source.AddHook(WindowProcedure);
        ApplyNativeAppearance();
    }

    private void ApplyNativeAppearance(bool refreshFrame=false)
    {
        if (closed || frame == null || source?.CompositionTarget == null) return;
        Background = Brushes.Transparent;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        frame.ApplyTheme(dark,refreshFrame&&IsVisible);
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
    }

    private void WindowLoaded(object sender, RoutedEventArgs args) => ApplyNativeAppearance();

    private void FirstContentRendered(object? sender, EventArgs args)
    {
        // WindowChrome's deferred template/frame initialization can run after
        // SourceInitialized. Restore the final transparent target after it settles.
        ApplyNativeAppearance();
        rendered = true;
        QueueReveal();
    }

    private void QueueReveal()
    {
        if (closed || revealed || !rendered || revealOperation is { Status: DispatcherOperationStatus.Pending }) return;
        // ContentRendered follows WPF's initial layout/render. Complete existing
        // dispatcher render work before uncloaking; no timer or empty panel phase.
        revealOperation = Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (closed || revealed || !IsVisible) return;
            ApplyNativeAppearance(refreshFrame:true);
            if (frame?.Reveal() == false)
            {
                firstReveal.TrySetException(new InvalidOperationException("The prepared material window could not be revealed."));
                return;
            }
            revealed = true;
            firstReveal.TrySetResult();
        }));
    }

    private nint WindowProcedure(nint hwnd, int message, nint wparam, nint lparam, ref bool handled)
    {
        if (closed || frame == null || hwnd != frame.Handle) return 0;
        if(message==0x0112&&((nuint)wparam&0xFFF0)==0xF020&&!minimizePending)
        {
            RequestMinimizeWithTransitionMaterial();handled=true;return 0;
        }
        if (frame.MaintainCustomCaption(message, wparam, lparam)) handled = true;
        else if (message == NativeWindowFrame.DwmCompositionChanged) ApplyNativeAppearance();
        // All sizing and position messages proceed to WPF's HwndTarget, which
        // performs its own synchronous CompleteRender during interactive resize.
        return 0;
    }

    private void WindowClosed(object? sender, EventArgs args)
    {
        closed = true;
        transitionOperation?.Abort();backdropOperation?.Abort();
        transitionBackdrop.Dispose();
        revealOperation?.Abort();
        ContentRendered -= FirstContentRendered;
        Loaded -= WindowLoaded;
        Closed -= WindowClosed;
        StateChanged -= TransitionStateChanged;
        source?.RemoveHook(WindowProcedure);
        frame?.Dispose();
        frame = null; source = null;
        firstReveal.TrySetCanceled();
    }
}
