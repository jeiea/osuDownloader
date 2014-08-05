/************************************************
단순한 2x2크기의 검은 영상 생성
************************************************/

BlankClip(length=1, width=4, height=4, color=$000000)
ConvertToRGB32(matrix="Rec601", interlaced=false)
KillAudio()