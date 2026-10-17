// See https://aka.ms/new-console-template for more information
using Edge_tts_sharp;
using Edge_tts_sharp.Model;

EdgeTts.Await = true;
var option = new PlayOption
{
    Rate = 1,
    Text = "",
    SavePath = "test.mp3"
};

Console.WriteLine("请输入文本内容.");
option.Text = Console.ReadLine();

var voice = EdgeTts.GetVoice().FirstOrDefault(i=> i.Name == "Microsoft Server Speech Text to Speech Voice (zh-CN, XiaoxiaoNeural)");
await EdgeTts.SaveAudioAsync(option, voice);

Console.WriteLine("已保存至 test.mp3");
