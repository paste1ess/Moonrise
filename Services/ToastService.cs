using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Text;

namespace Moonrise.Services
{
    internal interface IToastService
    {
        void AddToast(InfoBar toast);
        void DeleteToast();
        void DeleteToast(float time);
    }
    internal class ToastService// : IToastService
    {
    }
}
