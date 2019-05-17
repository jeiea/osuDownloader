# osuDownloader

오스 리듬게임의 비트맵 파일을 자동으로 받아주는 on screen display 프로그램…이었던 것.

개발 도중 HDD 손상으로 최근 커밋이 손실된 이후 개발 중단. 현재는 동작하지 않는 듯.

- EasyHook으로 ShellExecute 함수 후킹, 인게임에서 비트맵 링크를 열기를 탐지해 다운로드
- Direct3DHook으로 인게임 OSD 다운로드 상황 알림.

![메인 창 이미지](mainwindow.png)

R.I.P