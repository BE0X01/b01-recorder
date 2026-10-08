# B01 Recorder

Windows용 미니멀 화면 녹화 프로그램. 전체 화면, 창, 원하는 영역을 **MP4 · GIF · WebP**로 저장하세요.

[**Version 0.2 다운로드**](https://github.com/BE0X01/b01-recorder/releases/download/v0.2/b01-recorder-v0.2-win-x64.zip) · [릴리즈 보기](https://github.com/BE0X01/b01-recorder/releases/latest)

## Preview

![B01 Recorder — dark minimal interface](docs/images/preview.png)

## 주요 기능

- **세 가지 녹화 방식** — 전체 화면, 개별 창, 직접 지정한 영역
- **MP4 · GIF · WebP** — GIF와 WebP는 품질 조절 지원
- **프레임 설정** — 30 / 60 / 15 / 10 FPS 프리셋과 1–120 FPS 직접 입력
- **시스템 오디오** — MP4 녹화에 PC 소리 포함, 마우스 커서 표시 선택
- **명확한 녹화 영역** — 화면을 어둡게 하지 않는 청록색 테두리
- **녹화 파일 관리** — 썸네일, 앱 내 미리보기, 외부 재생, 폴더 열기, 휴지통 삭제
- **포터블 구성** — 설정과 캐시를 실행 파일 옆에 보관

## 시작하기

1. ZIP을 다운로드하고 압축을 풉니다.
2. `b01-recorder.exe`를 실행합니다. 함께 제공되는 `ffmpeg.exe`와 `ffprobe.exe`는 같은 폴더에 두세요.
3. `Full screen`, `Window`, `Region` 중 하나를 선택하고 녹화 대상을 지정합니다.
4. 형식, 프레임, 품질과 저장 폴더를 설정합니다. 기본 저장 위치는 바탕화면입니다.
5. `Record`를 눌러 시작하고, 같은 버튼이 `Stop & save`로 바뀌면 눌러 종료·저장합니다.

**대상을 선택해도 녹화는 시작되지 않습니다.** 녹화 중에도 프로그램 창은 최소화되지 않습니다.

저장 후 썸네일을 선택해 녹화 내용을 확인하세요. `Play in app`으로 재생하고, `Open externally`로 기본 플레이어에서 열 수 있습니다. `Delete`는 확인 후 파일을 휴지통으로 이동합니다.

## 실행 환경과 저장 위치

Windows 10/11 **x64**를 지원하며 .NET 런타임과 FFmpeg가 포함되어 있습니다. 앱 내 미리보기에는 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)이 필요합니다.

| 항목 | 저장 위치 |
| --- | --- |
| 녹화 파일 | 지정한 폴더 · 기본값은 바탕화면 |
| 마지막 사용 설정 | 실행 파일 옆 `setting.ini` · 프로그램 종료 시 한 번 저장 |
| 썸네일·플레이어 캐시 | 실행 파일 옆 `data` 폴더 |

시스템 오디오는 MP4에서 지원합니다. 창 녹화 중에는 대상 창을 최소화하지 마세요. 긴 GIF·WebP 녹화는 저장에 시간이 걸릴 수 있습니다.

## 라이선스

[MIT](LICENSE) · 포함된 FFmpeg와 기타 구성 요소의 라이선스는 [Third-party notices](THIRD-PARTY-NOTICES.md)를 참고하세요.
