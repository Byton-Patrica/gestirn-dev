{
  Gestirn dev 0.1 embedded terminal.

  The terminal intentionally starts one cmd.exe /C process per command. This
  keeps the first release small and makes it suitable for invoking local AI
  agents, build tools, Git and scripts without changing the existing compiler
  pipeline. A persistent interactive shell is planned for a later release.
}
unit GestirnTerminalFrm;

interface

uses
  Windows, Classes, SysUtils, Forms, Controls, StdCtrls, ExtCtrls;

type
  TGestirnTerminalForm = class;

  TTerminalCommandThread = class(TThread)
  private
    FOwner: TGestirnTerminalForm;
    FCommand: string;
    FOutputChunk: string;
    FProcessHandle: THandle;
    FExitCode: DWORD;
    procedure PublishOutput;
    procedure PublishFinished;
  protected
    procedure Execute; override;
  public
    constructor Create(AOwner: TGestirnTerminalForm; const Command: string);
    procedure Stop;
  end;

  TGestirnTerminalForm = class(TForm)
  private
    FOutput: TMemo;
    FCommand: TEdit;
    FRun: TButton;
    FStop: TButton;
    FStatus: TLabel;
    FThread: TTerminalCommandThread;
    procedure RunClick(Sender: TObject);
    procedure StopClick(Sender: TObject);
    procedure CommandKeyPress(Sender: TObject; var Key: Char);
    procedure FormClose(Sender: TObject; var Action: TCloseAction);
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure AppendOutput(const Text: string);
    procedure CommandFinished(ExitCode: DWORD);
  end;

implementation

constructor TTerminalCommandThread.Create(AOwner: TGestirnTerminalForm; const Command: string);
begin
  inherited Create(True);
  FreeOnTerminate := True;
  FOwner := AOwner;
  FCommand := Command;
  FProcessHandle := 0;
  FExitCode := 1;
  Resume;
end;

procedure TTerminalCommandThread.Stop;
begin
  Terminate;
  if FProcessHandle <> 0 then
    TerminateProcess(FProcessHandle, 1);
end;

procedure TTerminalCommandThread.PublishOutput;
begin
  if Assigned(FOwner) and (FOutputChunk <> '') then
    FOwner.AppendOutput(FOutputChunk);
end;

procedure TTerminalCommandThread.PublishFinished;
begin
  if Assigned(FOwner) then
    FOwner.CommandFinished(FExitCode);
end;

procedure TTerminalCommandThread.Execute;
var
  Security: TSecurityAttributes;
  StartupInfo: TStartupInfo;
  ProcessInfo: TProcessInformation;
  StdOutRead, StdOutWrite: THandle;
  Buffer: array[0..4095] of Char;
  BytesRead: DWORD;
  CommandLine: string;
  ReadResult: Boolean;
begin
  FillChar(Security, SizeOf(Security), 0);
  Security.nLength := SizeOf(Security);
  Security.bInheritHandle := True;

  StdOutRead := 0;
  StdOutWrite := 0;
  if not CreatePipe(StdOutRead, StdOutWrite, @Security, 0) then begin
    FOutputChunk := 'Gestirn dev terminal: unable to create output pipe.' + #13#10;
    Synchronize(PublishOutput);
    Synchronize(PublishFinished);
    Exit;
  end;
  SetHandleInformation(StdOutRead, HANDLE_FLAG_INHERIT, 0);

  FillChar(StartupInfo, SizeOf(StartupInfo), 0);
  StartupInfo.cb := SizeOf(StartupInfo);
  StartupInfo.dwFlags := STARTF_USESTDHANDLES or STARTF_USESHOWWINDOW;
  StartupInfo.wShowWindow := SW_HIDE;
  StartupInfo.hStdOutput := StdOutWrite;
  StartupInfo.hStdError := StdOutWrite;

  CommandLine := 'cmd.exe /C ' + FCommand;
  FillChar(ProcessInfo, SizeOf(ProcessInfo), 0);
  if not CreateProcess(nil, PChar(CommandLine), nil, nil, True,
    CREATE_NO_WINDOW, nil, nil, StartupInfo, ProcessInfo) then begin
    CloseHandle(StdOutRead);
    CloseHandle(StdOutWrite);
    FOutputChunk := 'Gestirn dev terminal: command could not be started.' + #13#10;
    Synchronize(PublishOutput);
    Synchronize(PublishFinished);
    Exit;
  end;

  CloseHandle(ProcessInfo.hThread);
  CloseHandle(StdOutWrite);
  FProcessHandle := ProcessInfo.hProcess;

  repeat
    ReadResult := ReadFile(StdOutRead, Buffer, SizeOf(Buffer) - 1, BytesRead, nil);
    if ReadResult and (BytesRead > 0) then begin
      Buffer[BytesRead] := #0;
      FOutputChunk := string(Buffer);
      Synchronize(PublishOutput);
    end;
  until (not ReadResult) or (BytesRead = 0) or Terminated;

  if Terminated then
    TerminateProcess(FProcessHandle, 1);
  WaitForSingleObject(FProcessHandle, INFINITE);
  GetExitCodeProcess(FProcessHandle, FExitCode);
  CloseHandle(FProcessHandle);
  FProcessHandle := 0;
  CloseHandle(StdOutRead);
  Synchronize(PublishFinished);
end;

constructor TGestirnTerminalForm.Create(AOwner: TComponent);
var
  TopPanel, BottomPanel: TPanel;
begin
  inherited Create(AOwner);
  Caption := 'Gestirn dev - Embedded terminal';
  Width := 860;
  Height := 520;
  Position := poScreenCenter;
  BorderStyle := bsSizeable;
  OnClose := FormClose;

  TopPanel := TPanel.Create(Self);
  TopPanel.Parent := Self;
  TopPanel.Align := alTop;
  TopPanel.Height := 32;
  TopPanel.BevelOuter := bvNone;

  FStatus := TLabel.Create(Self);
  FStatus.Parent := TopPanel;
  FStatus.Align := alClient;
  FStatus.Caption := 'Ready. Commands run through cmd.exe and can invoke local AI agents.';
  FStatus.Layout := tlCenter;

  FOutput := TMemo.Create(Self);
  FOutput.Parent := Self;
  FOutput.Align := alClient;
  FOutput.ReadOnly := True;
  FOutput.ScrollBars := ssBoth;
  FOutput.WordWrap := False;
  FOutput.Font.Name := 'Consolas';
  FOutput.Font.Size := 10;

  BottomPanel := TPanel.Create(Self);
  BottomPanel.Parent := Self;
  BottomPanel.Align := alBottom;
  BottomPanel.Height := 42;
  BottomPanel.BevelOuter := bvNone;

  FCommand := TEdit.Create(Self);
  FCommand.Parent := BottomPanel;
  FCommand.Align := alClient;
  FCommand.Text := '';
  FCommand.OnKeyPress := CommandKeyPress;
  FCommand.Hint := 'Command, for example: git status or your AI agent command';

  FRun := TButton.Create(Self);
  FRun.Parent := BottomPanel;
  FRun.Align := alRight;
  FRun.Width := 88;
  FRun.Caption := 'Run';
  FRun.OnClick := RunClick;

  FStop := TButton.Create(Self);
  FStop.Parent := BottomPanel;
  FStop.Align := alRight;
  FStop.Width := 88;
  FStop.Caption := 'Stop';
  FStop.Enabled := False;
  FStop.OnClick := StopClick;

  FThread := nil;
end;

destructor TGestirnTerminalForm.Destroy;
begin
  if Assigned(FThread) then begin
    // Do not wait here: the worker may be inside Synchronize and the main
    // thread must continue pumping messages while the process is terminated.
    FThread.FOwner := nil;
    FThread.Stop;
    FThread := nil;
  end;
  inherited Destroy;
end;

procedure TGestirnTerminalForm.AppendOutput(const Text: string);
begin
  FOutput.Lines.Text := FOutput.Lines.Text + Text;
  FOutput.SelStart := Length(FOutput.Text);
  FOutput.Perform(WM_VSCROLL, SB_BOTTOM, 0);
end;

procedure TGestirnTerminalForm.CommandFinished(ExitCode: DWORD);
begin
  FThread := nil;
  FRun.Enabled := True;
  FStop.Enabled := False;
  FCommand.Enabled := True;
  FStatus.Caption := Format('Command finished with exit code %d.', [ExitCode]);
end;

procedure TGestirnTerminalForm.RunClick(Sender: TObject);
var
  Command: string;
begin
  if Assigned(FThread) then
    Exit;
  Command := Trim(FCommand.Text);
  if Command = '' then
    Exit;
  AppendOutput('> ' + Command + #13#10);
  FStatus.Caption := 'Running...';
  FRun.Enabled := False;
  FStop.Enabled := True;
  FCommand.Enabled := False;
  FThread := TTerminalCommandThread.Create(Self, Command);
end;

procedure TGestirnTerminalForm.StopClick(Sender: TObject);
begin
  if Assigned(FThread) then
    FThread.Stop;
end;

procedure TGestirnTerminalForm.CommandKeyPress(Sender: TObject; var Key: Char);
begin
  if Key = #13 then begin
    Key := #0;
    RunClick(Sender);
  end;
end;

procedure TGestirnTerminalForm.FormClose(Sender: TObject; var Action: TCloseAction);
begin
  if Assigned(FThread) then
    FThread.Stop;
  Action := caFree;
end;

end.
