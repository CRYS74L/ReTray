using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using LanDeviceMonitor.Controls;
using ReTray_App;
internal static class Program
{
    [STAThread] static int Main(string[] args)
    {
        if (args.Contains("--hover-only"))
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try { HoverTests.Run(); return 0; }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
        int failed=0;
        foreach(var (name,test) in new (string,Action)[]{
            ("dropdown hover fades without dark-theme flashes or stale emphasis", RunHoverInIsolation),
            ("input method rows switch independently and retain selection",IndependentRows),
            ("selected replacement synchronizes the visible slider",SelectedFileUpdatesMode),
            ("locale switching preserves source labels and user bindings",LocaleRoundtrip),
            ("every input method balances heading and slider gaps",EqualHeadingSpacing),
            ("language order and saved codes stay independent",LanguageOrder),
            ("editor scrollbar preserves content width and positions",ScrollbarPreservesLayout),
            ("settings glyph ink is centered with a uniform thin outline",SettingsGlyphInk),
            ("P3 power matches approved proportions and has an unbroken arc",PowerMatchesPreview),
            ("page placeholders match controls and loading visibility",PagePlaceholders),
            ("list dividers keep constant coverage through fractional scrolling",DividerScrollStability),
            ("caption uses body TextBlock rendering and genuine Regular face",BrandCaptionAndVector),
            ("capital R aligns with the icon center", CaptionReferenceTests.Run),
            ("tray commands and selectors share dropdown appearance", SharedDropdownAppearance),
            ("tray menu anchors its visible lower left corner at the click", TrayPlacement),
            ("shutdown before runtime startup prevents a late native thread", RuntimeStartupCancellation)})
            try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
        return failed==0?0:1;
    }
    private static void RunHoverInIsolation()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--hover-only");
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Require(process.WaitForExit(20000), "Hover regression did not finish");
        Console.Write(output.GetAwaiter().GetResult());
        Require(process.ExitCode == 0, errors.GetAwaiter().GetResult());
    }
    private sealed class State : INotifyPropertyChanged
    {
        public ImageSource Original{get;}=new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null);
        public ImageSource AssetPreview{get;}=new WriteableBitmap(2,2,96,96,PixelFormats.Bgra32,null);
        private bool mode;
        public bool UseReplacement{get=>mode;set{mode=value;PropertyChanged?.Invoke(this,new(nameof(UseReplacement)));}}
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private static void SharedDropdownAppearance()
    {
        var app = Application.Current ?? new Application();
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        var shared = (ControlTemplate)app.Resources["DropdownSurfaceTemplate"];
        var combo = new ComboBox { Style = (Style)app.Resources[typeof(ComboBox)] }; combo.ApplyTemplate();
        var popup = (LanDeviceMonitor.Controls.AnimatedSelectPopup)combo.Template.FindName("PART_Popup", combo);
        Require(popup.Child is ContentControl selectorSurface && ReferenceEquals(selectorSurface.Template, shared),
            "Selector popup stopped using the shared dropdown frame");
        var menu = new ContextMenu { Style = (Style)app.Resources["TrayDropdownMenuStyle"] };
        menu.ApplyTemplate();
        Require(VisualTreeHelper.GetChild(menu, 0) is ContentControl traySurface && ReferenceEquals(traySurface.Template, shared),
            "Tray menu frame differs from the selector frame");
        var itemBase = (Style)app.Resources["DropdownItemBaseStyle"];
        Require(ReferenceEquals(combo.ItemContainerStyle.BasedOn, itemBase) &&
            ReferenceEquals(menu.ItemContainerStyle.BasedOn, itemBase), "Menu entries no longer share dropdown typography and motion");
        Require(menu.FontFamily.Source == combo.FontFamily.Source && menu.FontSize == combo.FontSize &&
            menu.FontWeight == combo.FontWeight, "Tray typography differs from selector typography");
        var source = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "WindowTemplates.xaml"));
        Require(!source.Descendants().Any(e => (string?)e.Attribute("MouseRightButtonUp") == "TrayTitleMenu"),
            "Title bar still exposes the tray menu");
    }
    private static void TrayPlacement() => TrayMenuPlacementTests.Run();
    private static void RuntimeStartupCancellation()
    {
        LanDeviceMonitor.Services.NativeRuntime.StopAsync().GetAwaiter().GetResult();
        try { LanDeviceMonitor.Services.NativeRuntime.StartAsync().GetAwaiter().GetResult(); }
        catch (OperationCanceledException) {
            Require(!LanDeviceMonitor.Services.NativeRuntime.IsActive, "A runtime thread escaped startup cancellation");
            return;
        }
        throw new Exception("A runtime was allowed to start after shutdown");
    }
    private static void BrandCaptionAndVector()
    {
        var app = Application.Current ?? new Application();
        var image = BrandArtwork.Image;
        Require(image.IsFrozen, "Brand artwork must be an immutable vector drawing");
        Require(Math.Abs(image.Width - 1024) < .001 && Math.Abs(image.Height - 1024) < .001,
            $"Vector artwork lost the full-bleed bounds: {image.Width} × {image.Height}");
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, 320, 320));
        var bitmap = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, "brand-vector-reference.png"))) encoder.Save(file);

        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        var family = (FontFamily)app.Resources["AppTextFont"];
        var face = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var resolved = face.TryGetGlyphTypeface(out var glyph);
        Console.WriteLine($"Caption actual family={family.Source}, base={family.BaseUri}, resolved={resolved}, simulations={glyph?.StyleSimulations}, uri={glyph?.FontUri}");
        Require(resolved && glyph!.StyleSimulations == StyleSimulations.None &&
            glyph.FontUri.ToString().EndsWith("/notosanssc-regular.ttf", StringComparison.OrdinalIgnoreCase),
            "Caption must use genuine Regular outlines");
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            var source = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "WindowTemplates.xaml"));
            var element = source.Descendants().Single(e => (string?)e.Attribute(System.Xml.Linq.XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "TitleBrandCaption");
            foreach (var attached in element.Attributes().Where(a => a.Name.LocalName == "CaptionLetterAlignment.Reference").ToArray()) attached.Remove();
            var caption = (TextBlock)System.Windows.Markup.XamlReader.Parse(element.ToString());
            Require(caption.GetType() == typeof(TextBlock) && ReferenceEquals(caption.Style, app.Resources["BodyTextStyle"]),
                "Caption must use the ordinary body TextBlock style without custom text drawing");
            var body = new TextBlock { Text = "ReTray", FontSize = 13, Style = (Style)app.Resources["BodyTextStyle"] };
            Require(TextOptions.GetTextRenderingMode(caption) == TextOptions.GetTextRenderingMode(body) &&
                TextOptions.GetTextFormattingMode(caption) == TextOptions.GetTextFormattingMode(body) &&
                TextOptions.GetTextHintingMode(caption) == TextOptions.GetTextHintingMode(body),
                "Caption rendering options differ from body text");
            var viewport = new Grid { Width = 100, Height = 32 };
            viewport.Children.Add(caption);
            VisualTreeHelper.SetRootDpi(viewport, new DpiScale(scale, scale));
            viewport.Measure(new Size(100, 32)); viewport.Arrange(new Rect(0, 0, 100, 32)); viewport.UpdateLayout();
            int width = (int)(100 * scale), height = (int)(32 * scale);
            var textBitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            textBitmap.Render(viewport);
            var bytes = new byte[width * height * 4]; textBitmap.CopyPixels(bytes, width * 4, 0);
            var rows = Enumerable.Range(0, height).Where(y => Enumerable.Range(0, width).Any(x => bytes[(y * width + x) * 4 + 3] > 32)).ToArray();
            Require(rows.Length > 0 && rows[^1] - rows[0] + 1 <= 14 * scale, "Caption text is oversized");
            double coverage = 0, moment = 0;
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                var alpha = bytes[(y * width + x) * 4 + 3];
                coverage += alpha; moment += alpha * (y + .5);
            }
            Require(rows[0] > 0 && rows[^1] < height - 1, "Caption descender is clipped");
            Require(moment / coverage > height / 4d && moment / coverage < height * .75,
                "Ordinary caption text escaped the title row");
            Require(bytes.Where((b, i) => i % 4 == 3 && b > 0 && b < 255).Count() > 25,
                "Caption lost antialiased edge coverage");
            encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(textBitmap));
            using var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"caption-{scale * 100}.png")); encoder.Save(file);
            Console.WriteLine($"Caption {scale * 100}%: rows {rows[0]}–{rows[^1]}, visual center {moment / coverage:F2}, logo center {height / 2d}");
        }
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
        Require((Color)app.Resources["SwitchOnColor"] == (Color)ColorConverter.ConvertFromString("#CF80FF"),
            "Enabled switch lost the soft brand lavender");
    }
    private static IconChoiceRow Row(State state)=>new(state,nameof(state.Original),()=>state.AssetPreview,
        ()=>state.UseReplacement,value=>state.UseReplacement=value,new RelayCommand(()=>{}));
    private static SegmentedControl Slider(IconChoiceRow row)=>((StackPanel)row.Content).Children.OfType<SegmentedControl>().Single();
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    private static void IndependentRows()
    {
        var a=new State();var b=new State();var first=Row(a);var second=Row(b);var remembered=a.AssetPreview;
        Slider(first).SelectedIndex=1;
        Require(a.UseReplacement && !b.UseReplacement && Slider(second).SelectedIndex==0,"Switching one input method changed another");
        Slider(first).SelectedIndex=0;
        Require(!a.UseReplacement && ReferenceEquals(remembered,a.AssetPreview),"Original mode erased the selected replacement");
        Slider(first).SelectedIndex=1;
        Require(a.UseReplacement && ReferenceEquals(remembered,a.AssetPreview),"Reselecting replacement lost the previous file");
    }
    private static void SelectedFileUpdatesMode()
    {
        var state=new State();var row=Row(state);row.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        state.UseReplacement=true;
        Require(Slider(row).SelectedIndex==1,"Model selected a custom file but slider still shows original mode");
        row.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }
    private static void LocaleRoundtrip()
    {
        var label=new TextBlock{Text="系统图标"};
        var user=new TextBlock();user.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding{Source="系统图标"});
        var root=new StackPanel();root.Children.Add(label);root.Children.Add(user);
        foreach(var pair in new[]{("zh-Hant","系統圖示"),("ja","システムアイコン"),("en","System icons"),("zh","系统图标")}) {
            ReTray_App.Services.Localization.CurrentLanguage=pair.Item1;
            ReTray_App.Services.Localization.Apply(root);
            Require(label.Text==pair.Item2,"Locale label did not roundtrip: "+pair.Item1);
            Require(user.Text=="系统图标" && System.Windows.Data.BindingOperations.IsDataBound(user,TextBlock.TextProperty),"Locale replaced user binding");
        }
    }
    private static void EqualHeadingSpacing()
    {
        foreach(int count in new[]{0,1,2,5}) {
            var model=new InputMethodIconEditorModel(new ReTray_App.Services.PipeClient());
            for(int i=0;i<count;i++)model.Rows.Add((InputMethodIconRow)Activator.CreateInstance(typeof(InputMethodIconRow),System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{model,new System.Text.Json.Nodes.JsonObject {
                ["original"]=new System.Text.Json.Nodes.JsonObject { ["width"]=1,["height"]=1,["pixels"]="AAAA/w==" },
                ["key"]="test-"+i,["name"]=i%3==0?"微信输入法":i%3==1?"Google Japanese Input":"日本語入力",["policy"]=new System.Text.Json.Nodes.JsonObject {
                    ["asset"]="",["index"]=0,["useReplacement"]=false }
            }},null)!);
            var editor=new InputMethodIconEditor(model,()=>System.Threading.Tasks.Task.CompletedTask);
            var window=new Window{Content=editor,Width=400,SizeToContent=SizeToContent.Height,ShowActivated=false,ShowInTaskbar=false,Opacity=0};window.Show();window.UpdateLayout();
            var root=(Grid)editor.Content;
            var scroll=root.Children.OfType<ScrollViewer>().Single();
            var content=(StackPanel)scroll.Content;
            var fixedHeader=root.Children.OfType<Grid>().SingleOrDefault();
            Require(fixedHeader!=null,"Close header is still inside the clipped scrolling viewport");
            var titles=new[]{fixedHeader!}.Concat(content.Children.OfType<Grid>()).ToArray();
            var choices=content.Children.OfType<IconChoiceRow>().ToArray();
            Require(choices.Length==count,"Editor did not represent every input method");
            Require(titles.Length==Math.Max(1,count),"Input methods did not share the heading component");
            for(int i=0;i<count;i++) {
                var title=titles[i].Children.OfType<TextBlock>().Single();
                title.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                for(int pass=0;pass<3;pass++){editor.Measure(new Size(400,double.PositiveInfinity));editor.Arrange(new Rect(0,0,400,editor.DesiredSize.Height));editor.UpdateLayout();}
                var image=Find<Image>(choices[i]).First();
                var mode=Slider(choices[i]);
                var ink=new System.Windows.Media.FormattedText(title.Text,System.Globalization.CultureInfo.CurrentCulture,title.FlowDirection,
                    new Typeface(title.FontFamily,title.FontStyle,title.FontWeight,title.FontStretch),title.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(title).PixelsPerDip) {MaxTextWidth=title.ActualWidth,MaxLineCount=1,Trimming=TextTrimming.CharacterEllipsis}.BuildGeometry(new Point()).Bounds;
                double top=image.TranslatePoint(new Point(),root).Y-title.TranslatePoint(new Point(ink.Left,ink.Bottom),root).Y;
                var caption=Find<TextBlock>(choices[i]).First(t=>t.Text=="原始图标");
                var captionInk=new FormattedText(caption.Text,System.Globalization.CultureInfo.CurrentCulture,caption.FlowDirection,new Typeface(caption.FontFamily,caption.FontStyle,caption.FontWeight,caption.FontStretch),caption.FontSize,Brushes.Black,VisualTreeHelper.GetDpi(caption).PixelsPerDip).BuildGeometry(new Point()).Bounds;
                double bottom=mode.TranslatePoint(new Point(),root).Y-caption.TranslatePoint(new Point(captionInk.Left,captionInk.Bottom),root).Y;
                Require(Math.Abs(top-bottom)<.5,"Heading and slider gaps differ in row "+i+": "+top+" vs "+bottom);
            }
            Require(titles.SelectMany(h=>Find<Button>(h)).Count(b=>b.Visibility==Visibility.Visible)==1,"Editor needs exactly one close button");window.Close();
        }
    }
    private static IEnumerable<T> Find<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++) {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T typed)yield return typed;
            foreach(var nested in Find<T>(child))yield return nested;
        }
    }
    private static void LanguageOrder()
    {
        var names=ReTray_App.Services.Localization.LanguageNames;
        Require(names.SequenceEqual(new[]{"简体中文","繁體中文","日本語","English"}),"Language dropdown order differs");
        foreach(var code in new[]{"zh","zh-Hant","ja","en"})
            Require(ReTray_App.Services.Localization.LanguageCodes[ReTray_App.Services.Localization.LanguageIndex(code)]==code,"Saved language code changed meaning");
        Require(ReTray_App.Services.Localization.LanguageIndex(null)==0,"Default language must be simplified Chinese");
    }
    private static void ScrollbarPreservesLayout()
    {
        var app=new LanDeviceMonitor.App();app.InitializeComponent();
        // Render the actual template without loading page events or connecting to the agent.
        var document=System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"EditorTemplates.xaml"));
        var key=System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml")+"Key";
        var template=document.Descendants().Single(e=>e.Name.LocalName=="Style" && (string?)e.Attribute(key)=="IconEditorScrollViewerStyle");
        template.Attribute("BasedOn")!.Remove();
        var style=(Style)System.Windows.Markup.XamlReader.Parse(template.ToString());
        var choice=Row(new State());
        var stack=new StackPanel();stack.Children.Add(choice);stack.Children.Add(new Border{Height=450});
        var scroll=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Style=style};
        var window=new Window{Content=new Border{Padding=new Thickness(20),Child=scroll},Width=400,Height=800,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
        try {
            window.Show();window.UpdateLayout();
            Require(scroll.ComputedVerticalScrollBarVisibility==Visibility.Collapsed,"Tall editor unexpectedly scrolls");
            var image=Find<Image>(choice).First();var mode=Slider(choice);
            var viewport=scroll.ViewportWidth;
            var position=image.TranslatePoint(new Point(),scroll);
            var modePosition=mode.TranslatePoint(new Point(),scroll);
            var width=mode.ActualWidth;
            window.Height=300;window.UpdateLayout();
            Require(scroll.ComputedVerticalScrollBarVisibility==Visibility.Visible,"Short editor does not expose scrollbar");
            Require(Math.Abs(scroll.ViewportWidth-viewport)<.01,"Scrollbar reduced editor viewport width");
            Require((image.TranslatePoint(new Point(),scroll)-position).Length<.01,"Scrollbar moved the original icon");
            Require((mode.TranslatePoint(new Point(),scroll)-modePosition).Length<.01 && Math.Abs(mode.ActualWidth-width)<.01,"Scrollbar changed slider bounds");
            var bar=(System.Windows.Controls.Primitives.ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar",scroll);
            Require(bar.ActualWidth>0 && bar.TranslatePoint(new Point(),scroll).X>=scroll.ViewportWidth,"Scrollbar overlaps editor content");
            var hit=window.InputHitTest(bar.TranslatePoint(new Point(bar.ActualWidth/2,bar.ActualHeight/2),window)) as DependencyObject;
            while(hit!=null && hit!=bar)hit=VisualTreeHelper.GetParent(hit);
            Require(hit==bar,"Scrollbar cannot be operated in the card inset");
        }
        finally{window.Close();}
    }
    private static void SettingsGlyphInk()
    {
        foreach(var kind in Enum.GetValues<SettingsIconKind>()) {
            var icon=new SettingsIcon{Kind=kind,Width=28,Height=28,Foreground=Brushes.Black};
            icon.Measure(new Size(28,28));icon.Arrange(new Rect(0,0,28,28));
            var bitmap=new RenderTargetBitmap(280,280,960,960,PixelFormats.Pbgra32);bitmap.Render(icon);
            var pixels=new byte[280*280*4];bitmap.CopyPixels(pixels,280*4,0);
            int left=280,right=-1,top=280,bottom=-1;
            for(int y=0;y<280;y++)for(int x=0;x<280;x++)if(pixels[(y*280+x)*4+3]>20){left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);}
            Require(right>left,"Missing settings glyph "+kind);
            Require(Math.Abs((left+right)/2d-139.5)<=.5,"Settings glyph not horizontally centered: "+kind);
            Require(Math.Abs((top+bottom)/2d-139.5)<=.5,"Settings glyph not vertically centered: "+kind);
            if(kind==SettingsIconKind.Startup) {
                for(int y=75;y<=125;y+=10)
                    Require(pixels[(y*280+139)*4+3]>180,"Startup must show the selected long vertical power stem");
                double coverage=0;for(int x=120;x<160;x++)coverage+=pixels[(80*280+x)*4+3]/255d/10;
                Require(Math.Abs(coverage-1)<.15,"Power stem outline differs from one DIP: "+coverage);
            }
        }
    }
    private static void PagePlaceholders()
    {
        var x=System.Xml.Linq.XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var source=System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"WindowTemplates.xaml"));
        var dictionary=new System.Xml.Linq.XElement(source.Root!.Name.Namespace+"ResourceDictionary",
            source.Root.Attributes().Where(a=>a.IsNamespaceDeclaration).Select(a=>new System.Xml.Linq.XAttribute(a.Name,
                a.Value.StartsWith("clr-namespace:") && !a.Value.Contains(";assembly=")?a.Value+";assembly=ReTray":a.Value)),
            source.Descendants().Where(e=>e.Name.LocalName=="Style" &&
                new[]{"DeviceListScrollViewerStyle","DeviceListBoxStyle","FooterCircleButtonStyle"}.Contains((string?)e.Attribute(x+"Key"))));
        // MainPage supplies its own item container, so omit the LAN-specific one.
        dictionary.Elements().Single(e=>(string?)e.Attribute(x+"Key")=="DeviceListBoxStyle")
            .Elements().Where(e=>(string?)e.Attribute("Property") is "ItemContainerStyle" or "ItemTemplate").ToList().ForEach(e=>e.Remove());
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(dictionary.ToString()));
        var page=new MainPage();
        FrameworkElement Element(string name)=>(FrameworkElement)page.FindName(name);
        void Layout(double height=640){page.Measure(new Size(364,height));page.Arrange(new Rect(0,0,364,height));page.UpdateLayout();}
        var navigation=(SegmentedControl)Element("Navigation");
        Layout();
        var tray=(StackPanel)Element("LoadingRows");
        var first=(Grid)tray.Children[0];
        Require(first.ColumnDefinitions[0].ActualWidth==28,"Tray skeleton uses an obsolete icon column");
        Require(Find<Border>(first).Any(b=>Equals(b.Tag,"PlaceholderMore")),"Tray skeleton omits more action");
        Require(tray.Children.Count>=Math.Ceiling(Element("PagesHost").ActualHeight/34),"Tray skeleton leaves an empty lower viewport");
        navigation.SelectedIndex=1;Layout();
        Require(Element("SystemLoadingRows").Visibility==Visibility.Visible,"System loading state exposes unfinished controls");
        var system=(StackPanel)Element("SystemLoadingRows");
        Require(system.Children.OfType<Grid>().Count()==7,"System skeleton has an obsolete row count");
        Require(Find<Border>(system).Count(b=>Equals(b.Tag,"PlaceholderMore"))==1,"Only the input method should have a more placeholder");
        Require(Element("SystemList").Visibility==Visibility.Collapsed,"Real system controls overlap skeletons");
        navigation.SelectedIndex=2;Layout();
        var settings=(StackPanel)Element("SettingsLoadingRows");
        Require(settings.Visibility==Visibility.Visible && settings.Children.OfType<Grid>().Count()==4,"Settings skeleton does not match four rows");
        Require(Find<Border>(settings).Count(b=>Equals(b.Tag,"PlaceholderSelector"))==2,"Theme/language selector placeholders missing");
        Require(Find<Border>(settings).Count(b=>Equals(b.Tag,"PlaceholderExport") || Equals(b.Tag,"PlaceholderImport"))==2,"Configuration capsules missing");
        var buttonPlaceholder=Find<Border>(settings).Single(b=>Equals(b.Tag,"PlaceholderImport"));
        var expected=buttonPlaceholder.TranslatePoint(new Point(),page);
        var expectedSize=buttonPlaceholder.RenderSize;
        page.Vm.IsLoading=false;Layout();
        Require(settings.Visibility==Visibility.Collapsed && Element("SettingsRows").Visibility==Visibility.Visible,"Loading completion leaves skeletons over real controls");
        var button=Element("ImportConfigurationButton");
        Require((button.TranslatePoint(new Point(),page)-expected).Length<.1 && Math.Abs(button.ActualWidth-expectedSize.Width)<.1 && Math.Abs(button.ActualHeight-expectedSize.Height)<.1,"Configuration skeleton and real capsule do not align");
        page.Vm.Error="Synthetic connection error";Layout();
        Require(Element("ErrorNotice").Visibility==Visibility.Visible && settings.Visibility==Visibility.Collapsed,"Connection error is hidden behind permanent placeholders");
        page.Vm.Error="";page.Vm.IsLoading=true;Layout();
        Require(settings.Visibility==Visibility.Visible && !settings.IsHitTestVisible,"Reconnect skeleton exposes interactive controls");
        // Periodic system reads retain completed rows; only the first read uses silhouettes.
        page.Vm.IsLoading=false;navigation.SelectedIndex=1;
        var fields=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        typeof(MainPage).GetField("systemLoading",fields)!.SetValue(page,true);
        typeof(MainPage).GetMethod("UpdatePresentation",fields)!.Invoke(page,null);Layout();
        Require(Element("SystemLoadingRows").Visibility==Visibility.Visible,"First system read skips its loading placeholders");
        typeof(MainPage).GetField("systemLoaded",fields)!.SetValue(page,true);
        typeof(MainPage).GetMethod("UpdatePresentation",fields)!.Invoke(page,null);Layout();
        Require(Element("SystemLoadingRows").Visibility==Visibility.Collapsed && Element("SystemList").Visibility==Visibility.Visible,"Periodic system reads flicker back to placeholders");
        typeof(MainPage).GetField("systemLoading",fields)!.SetValue(page,false);
        foreach(var language in new[]{"zh","zh-Hant","ja","en"}) {
            ReTray_App.Services.Localization.CurrentLanguage=language;
            ReTray_App.Services.Localization.Apply(page);
            typeof(MainPage).GetMethod("CreateLoadingRows",fields)!.Invoke(page,null);
            page.Vm.IsLoading=true;navigation.SelectedIndex=2;Layout();
            var silhouette=Find<Border>(settings).Single(b=>Equals(b.Tag,"PlaceholderImport"));
            var position=silhouette.TranslatePoint(new Point(),page);var size=silhouette.RenderSize;
            page.Vm.IsLoading=false;Layout();
            Require((button.TranslatePoint(new Point(),page)-position).Length<.1 && Math.Abs(button.ActualWidth-size.Width)<.1,"Localized capsule size differs from loading silhouette: "+language);
        }
        ReTray_App.Services.Localization.CurrentLanguage="zh";ReTray_App.Services.Localization.Apply(page);
        typeof(MainPage).GetMethod("CreateLoadingRows",fields)!.Invoke(page,null);
        if(Environment.GetEnvironmentVariable("TRAY_PLACEHOLDER_SNAPSHOTS") is {Length:>0} output) {
            System.IO.Directory.CreateDirectory(output);page.Vm.IsLoading=true;
            foreach(var theme in new[]{"Light","Dark"}) {
                LanDeviceMonitor.Services.ThemeService.Apply(theme);
                // Build a fresh, unloaded page in the palette. Pumping the app
                // dispatcher would run its production startup, outside this harness.
                var visualPage=new MainPage();
                var visualNavigation=(SegmentedControl)visualPage.FindName("Navigation");
                foreach(int index in new[]{0,1,2}) {
                    visualNavigation.SelectedIndex=index;
                    visualPage.Measure(new Size(364,640));visualPage.Arrange(new Rect(0,0,364,640));visualPage.UpdateLayout();
                    var color=((SolidColorBrush)Find<Border>((StackPanel)visualPage.FindName("SettingsLoadingRows")).First(b=>Equals(b.Tag,"PlaceholderIcon")).Background).Color;
                    Require(theme=="Dark"?color.R>200:color.R<100,"Placeholder palette is invisible in "+theme);
                    var bitmap=new RenderTargetBitmap(455,800,120,120,PixelFormats.Pbgra32);
                    var background=new DrawingVisual();using(var draw=background.RenderOpen())draw.DrawRectangle(
                        new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme=="Light"?"#F8F8FA":"#242424")),null,new Rect(0,0,364,640));
                    bitmap.Render(background);bitmap.Render(visualPage);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file=System.IO.File.Create(System.IO.Path.Combine(output,$"{theme.ToLowerInvariant()}-{index}.png"));encoder.Save(file);
                }
            }
        }
        LanDeviceMonitor.Services.ThemeService.Apply("Light");
    }
    private static void DividerScrollStability()
    {
        var source=System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"EditorTemplates.xaml"));
        var xml=new System.Xml.Linq.XElement(source.Descendants().Single(e=>(string?)e.Attribute("Grid.ColumnSpan")=="3" && (string?)e.Attribute("Height")=="0.5"));
        if(xml.Name.NamespaceName.StartsWith("clr-namespace:"))
            xml.Name=System.Xml.Linq.XNamespace.Get(xml.Name.NamespaceName+";assembly=ReTray")+xml.Name.LocalName;
        var wrapper=new System.Xml.Linq.XElement(source.Root!.Name.Namespace+"Grid",
            source.Root.Attributes().Where(a=>a.IsNamespaceDeclaration).Select(a=>new System.Xml.Linq.XAttribute(a.Name,
                a.Value.StartsWith("clr-namespace:")?a.Value+";assembly=ReTray":a.Value)),xml);
        xml.SetAttributeValue("Background","White");
        foreach(double scale in new[]{1d,1.25,1.5,2d})foreach(byte alpha in new byte[]{176,48}) {
            var parsed=(Grid)System.Windows.Markup.XamlReader.Parse(wrapper.ToString());
            var line=(Border)parsed.Children[0];parsed.Children.Remove(line);
            line.Width=200;line.Background=new SolidColorBrush(Color.FromArgb(alpha,168,168,168));
            var mask=new LinearGradientBrush();mask.GradientStops.Add(new GradientStop(Colors.Transparent,0));
            mask.GradientStops.Add(new GradientStop(Colors.White,.2));mask.GradientStops.Add(new GradientStop(Colors.White,.8));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent,1));
            var viewport=new Canvas{Width=240,Height=40,OpacityMask=mask};viewport.Children.Add(line);
            VisualTreeHelper.SetRootDpi(viewport,new DpiScale(scale,scale));
            double min=double.MaxValue,max=0,minPeak=double.MaxValue,maxPeak=0;
            for(int phase=0;phase<24;phase++) {
                Canvas.SetTop(line,16+phase*.1/scale);
                viewport.Measure(new Size(240,40));viewport.Arrange(new Rect(0,0,240,40));viewport.UpdateLayout();
                Require(Math.Abs(VisualTreeHelper.GetDpi(line).DpiScaleY-scale)<.001,"Test DPI did not propagate");
                int width=(int)Math.Ceiling(240*scale),height=(int)Math.Ceiling(40*scale);
                var bitmap=new RenderTargetBitmap(width,height,96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(viewport);
                var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
                double coverage=0,peak=0;int x=(int)(80*scale);
                for(int y=0;y<height;y++){var a=pixels[(y*width+x)*4+3];coverage+=a;peak=Math.Max(peak,a);}
                min=Math.Min(min,coverage);max=Math.Max(max,coverage);minPeak=Math.Min(minPeak,peak);maxPeak=Math.Max(maxPeak,peak);
                Require(line.ActualHeight==.5,"Divider repair changed layout spacing");
            }
            Console.WriteLine($"Divider {scale*100}% alpha {alpha}: coverage {min}-{max}, peak {minPeak}-{maxPeak}");
            Require(min>0 && max-min<=2 && maxPeak-minPeak<=2,"Divider disappears or flashes with subpixel scroll offsets");
        }
    }
    private static void PowerMatchesPreview()
    {
        var reference=new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures/p3-power-preview.png")));
        var rgba=new FormatConvertedBitmap(reference,PixelFormats.Bgra32,null,0);
        var expected=new byte[rgba.PixelWidth*rgba.PixelHeight*4];rgba.CopyPixels(expected,rgba.PixelWidth*4,0);
        (int left,int right,int top,int bottom) Bounds(byte[] pixels,int width,int height) {
            int l=width,r=-1,t=height,b=-1;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(pixels[(y*width+x)*4+3]>16){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
            return(l,r,t,b);
        }
        var target=Bounds(expected,rgba.PixelWidth,rgba.PixelHeight);
        foreach(double dpiScale in new[]{1d,1.25,1.5,2,10}) {
            var icon=new SettingsIcon{Kind=SettingsIconKind.Startup,Width=28,Height=28,Foreground=Brushes.Black};
            icon.Measure(new Size(28,28));icon.Arrange(new Rect(0,0,28,28));
            int size=(int)Math.Round(28*dpiScale);
            var bitmap=new RenderTargetBitmap(size,size,96*dpiScale,96*dpiScale,PixelFormats.Pbgra32);bitmap.Render(icon);
            var pixels=new byte[size*size*4];bitmap.CopyPixels(pixels,size*4,0);
            var actual=Bounds(pixels,size,size);
            // The user requested a small enlargement of the faithful P3 size.
            double wantedWidth=(target.right-target.left+1)*21.5/rgba.PixelWidth;
            Console.WriteLine($"P3 DPI {dpiScale}: width {(actual.right-actual.left+1)/dpiScale:F2}, expected {wantedWidth:F2}");
            // Alpha bounds include the antialias fringe at each edge.
            double rasterTolerance=1.5/dpiScale+.12;
            double wantedHeight=(target.bottom-target.top+1)*21.5/rgba.PixelHeight;
            var visited=new bool[size*size];int components=0;
            for(int index=0;index<visited.Length;index++) {
                if(visited[index]||pixels[index*4+3]<=16)continue;
                components++;var queue=new Queue<int>();queue.Enqueue(index);visited[index]=true;
                while(queue.Count>0){int p=queue.Dequeue(),x=p%size,y=p/size;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++){int xx=x+dx,yy=y+dy;if(xx<0||xx>=size||yy<0||yy>=size)continue;int n=yy*size+xx;if(!visited[n]&&pixels[n*4+3]>16){visited[n]=true;queue.Enqueue(n);}}
                }
            }
            Console.WriteLine($"P3 DPI {dpiScale}: connected parts {components}");
            Require(components==2,"Power arc is cut into extra pieces at DPI "+dpiScale+": "+components);
            Require(Math.Abs((actual.right-actual.left+1)/dpiScale-wantedWidth)<rasterTolerance,"P3 footprint does not match selected preview at DPI "+dpiScale);
            Require(Math.Abs((actual.bottom-actual.top+1)/dpiScale-wantedHeight)<rasterTolerance,"P3 height does not match selected preview at DPI "+dpiScale);
        }
    }
}
