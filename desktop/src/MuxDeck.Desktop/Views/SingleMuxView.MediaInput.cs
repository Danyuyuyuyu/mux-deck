using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MuxDeck.Desktop.ViewModels;

namespace MuxDeck.Desktop.Views;

public partial class SingleMuxView
{
    private void PickMedia(InputOperation operation, TrackRowViewModel? replacing = null) => BeginMediaInput(async () =>
        ReadStorageItems(await Interactions.PickAsync(TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("窗口不可用。"), operation)), replacing);
    private void BeginMediaInput(Func<Task<IReadOnlyList<InputCandidate>>> gather, TrackRowViewModel? replacing = null)
    {
        if (Model is not { CanEdit: true } model || acceptingInput) { Model?.ReportInteractionMessage("正在处理输入或任务，当前不能接纳新的输入。"); return; }
        acceptingInput = true;
        InputTask = ProcessMediaInputAsync(model, model.DraftRevision, gather, replacing);
    }
    private async Task ProcessMediaInputAsync(SingleMuxViewModel model, long revision, Func<Task<IReadOnlyList<InputCandidate>>> gather, TrackRowViewModel? replacing)
    {
        bool Current() => ReferenceEquals(Model, model) && model.CanEdit && model.DraftRevision == revision;
        try
        {
            var candidates = await gather(); if (candidates.Count == 0) return;
            if (!Current()) { model.ReportInteractionMessage("操作返回时任务或草稿已变化，原输入保持。"); return; }
            if (replacing is not null && candidates.Count == 1 && string.Equals(Path.GetFullPath(candidates[0].Path), Path.GetFullPath(replacing.InputPath), StringComparison.OrdinalIgnoreCase)) return;
            model.ReportInteractionMessage("正在识别文件类型和轨道…", NotificationKind.Busy);
            var result = await model.PrepareMediaInputAsync(candidates);
            if (!result.Succeeded) { model.ReportInteractionMessage(result.Message); return; }
            if (!Current()) { model.ReportInteractionMessage("识别期间任务或草稿已变化，整批输入保持。"); return; }
            var plan = result.Plan!; var owner = TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException("窗口不可用。");
            if (replacing is not null)
            {
                if (plan.Video is not null || plan.Fonts is not null || plan.Tracks.Count != 1 || (plan.Tracks[0].Media.Type == "audio") != replacing.IsAudio)
                { model.ReportInteractionMessage("更换文件必须只包含一条同类型轨道；多音轨文件请通过添加入口导入。"); return; }
                if (!await Interactions.ConfirmTrackAsync(owner, replacing.InputPath, plan.Tracks[0].Path) || !Current()) return;
                if (model.ReplaceImportedTrack(replacing, plan.Tracks[0]))
                    model.ReportInteractionMessage("已更换轨道文件，元数据保持。", NotificationKind.Success);
            }
            else
            {
                var confirmed = false;
                if (plan.Video is not null && model.HasVideo && !string.Equals(Path.GetFullPath(model.VideoPath), plan.Video, StringComparison.OrdinalIgnoreCase))
                { confirmed = await Interactions.ConfirmVideoAsync(owner, model.VideoPath, plan.Video); if (!confirmed) return; }
                if (!Current()) { model.ReportInteractionMessage("确认期间草稿已变化，整批输入保持。"); return; }
                var committed = model.CommitMediaInput(plan, confirmed); if (!committed.Succeeded) model.ReportInteractionMessage(committed.Message);
            }
        }
        catch (Exception ex) { model.ReportInteractionMessage("输入未接纳：" + ex.Message); }
        finally
        {
            // 用户取消确认或选择不属于错误，但不能遗留永不消失的识别状态。
            if (model.Notification.IsBusy) model.ReportInteractionMessage("已取消添加，原输入保持。", NotificationKind.Information);
            acceptingInput = false; ClearDropFeedback();
        }
    }
}
