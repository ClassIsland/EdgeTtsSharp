# Edge_tts_sharp 
[Edge_tts_sharp](https://www.nuget.org/packages/Edge_tts_sharp)，是一个免费的C#库，可以调用Edge浏览器的文本转语音功能。

## install
```sh
NuGet\Install-Package Edge_tts_sharp -Version 1.0.2
```

## 文字转语言
```cs
using Edge_tts_sharp;

string msg = string.Empty;
Console.WriteLine("请输入文本内容.");
msg = Console.ReadLine();
// 1.0
//Edge_tts.PlayText(msg, "zh-CN", "Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)", "webm-24khz-16bit-mono-opus");
var voice = Edge_tts.GetVoice().FirstOrDefault(i=> i.Name == "Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)");
Edge_tts.PlayText(msg, voice);
Console.ReadLine();
```

## 设置语速
```cs
// 文字转语音，并且设置语速
Edge_tts.PlayText("你好微软！", "Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)", -25);
```

## 保存到本地
```cs
// 文字转语音，并且设置语速
Edge_tts.PlayText("你好微软！", "Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)", -25, "c:\\audio\\xxx.mp3");
```

## 获取音频列表
```cs
using Edge_tts_sharp;

var voices = Edge_tts.GetVoice();
foreach(var item in voices){
    Console.WriteLine($"voice name is{item.Name}, locale（语言） is {item.Locale}, SuggestedCodec(音频类型) is {item.SuggestedCodec}");
}
```

## 更新内容

- 2023.10.28
    - 第一次上传。
- 2023.10.30
    - 更新调用接口的方式