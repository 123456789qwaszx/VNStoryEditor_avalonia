using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using System;
using Vn.App.Services;

namespace Vn.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = CreateMainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 주 창을 만들지 못해도 아무 말 없이 사라지지 않는다.
    /// 창이 하나도 없으면 프로세스가 그대로 끝나 작가는 원인을 알 수 없다.
    /// 그래서 실패는 파일로 남기고, 무슨 일이 있었는지 보여주는 창을 대신 띄운다.
    /// </summary>
    private static Window CreateMainWindow()
    {
        try
        {
            return new MainWindow();
        }
        catch (Exception exception)
        {
            StartupLog.TryWrite("주 창 만들기", exception);
            return CreateStartupFailureWindow(exception);
        }
    }

    private static Window CreateStartupFailureWindow(Exception exception)
    {
        var details = new TextBox
        {
            Text = exception.ToString(),
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono,Consolas"),
            FontSize = 12,
            Height = 260
        };

        ScrollViewer.SetHorizontalScrollBarVisibility(details, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(details, ScrollBarVisibility.Auto);

        return new Window
        {
            Title = "VnTool — 시작하지 못했습니다",
            Width = 760,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        // 작가용 메시지. 개발자용 상세 내용은 아래 상자와 로그 파일에 있다.
                        Text = StartupLog.WriterMessage("VnTool을 시작하는"),
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeight.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "아래 내용을 그대로 개발자에게 전달해 주세요.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.7
                    },
                    details
                }
            }
        };
    }
}
