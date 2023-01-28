# RealtimeTranscribe
real-time transcription application

リアルタイム文字起こしを行うWindowsのアプリケーションです。

マイクからの音声とパソコンで再生している音声を認識できます。

Whisperのbaseモデルを使用しています。

![image](https://user-images.githubusercontent.com/7104722/215273234-615a9cc2-d121-4c09-814e-9c3193b38d4d.png)

## インストール
[Release](https://github.com/TadaoYamaoka/RealtimeTranscribe/releases)からRealtimeTranscribe.zipをダウンロードして、任意のフォルダに解凍します。

## 実行方法
解凍したフォルダにある「RealtimeTranscribe.exe」をダブルクリックすると起動できます。

## 実行環境
[.NET 6のランタイム](https://dotnet.microsoft.com/ja-jp/download/dotnet/6.0)が必要

## 使用ライブラリ
以下のライブラリを使用しています。
* [ONNX Runtime](https://github.com/Microsoft/onnxruntime)
* [NAudio](https://github.com/naudio/NAudio)
* [Math.NET Numerics](https://numerics.mathdotnet.com/)

## ライセンス
<a rel="license" href="http://creativecommons.org/licenses/by-nc/4.0/"><img alt="クリエイティブ・コモンズ・ライセンス" style="border-width:0" src="https://i.creativecommons.org/l/by-nc/4.0/88x31.png"></a>
