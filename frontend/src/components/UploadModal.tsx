"use client";

import * as React from "react";
import { X, UploadSimple, File, Trash, Lightning, CheckCircle, Warning, SpinnerGap } from "@phosphor-icons/react";
import { motion, AnimatePresence } from "framer-motion";
import { cn } from "@/lib/utils";

interface FileItem {
  file: File;
  status: "pending" | "processing" | "done" | "error";
  message?: string;
  chunkCount?: number;
}

interface UploadModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const ACCEPTED_EXTENSIONS = [".pdf", ".txt", ".json"];
const BACKEND_URL = process.env.NEXT_PUBLIC_DOTNET_API_URL?.replace("/api/chat", "") || "http://localhost:5000";

export const UploadModal: React.FC<UploadModalProps> = ({ isOpen, onClose }) => {
  const [files, setFiles] = React.useState<FileItem[]>([]);
  const [isProcessing, setIsProcessing] = React.useState(false);
  const [overallProgress, setOverallProgress] = React.useState({ current: 0, total: 0 });
  const [fakeProgress, setFakeProgress] = React.useState(0);
  const [currentStepMessage, setCurrentStepMessage] = React.useState("");
  const fileInputRef = React.useRef<HTMLInputElement>(null);

  const isValidFile = (file: File) => {
    const ext = "." + file.name.split(".").pop()?.toLowerCase();
    return ACCEPTED_EXTENSIONS.includes(ext);
  };

  const handleFilesSelected = (selectedFiles: FileList | null) => {
    if (!selectedFiles) return;
    const validFiles = Array.from(selectedFiles)
      .filter(isValidFile)
      .filter((f) => !files.some((existing) => existing.file.name === f.name))
      .map((file) => ({ file, status: "pending" as const }));
    setFiles((prev) => [...prev, ...validFiles]);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    handleFilesSelected(e.dataTransfer.files);
  };

  const handleRemoveFile = (index: number) => {
    setFiles((prev) => prev.filter((_, i) => i !== index));
  };

  const handleStartProcessing = async () => {
    if (files.length === 0 || isProcessing) return;
    setIsProcessing(true);
    setOverallProgress({ current: 0, total: files.length });

    for (let i = 0; i < files.length; i++) {
      setFiles((prev) =>
        prev.map((f, idx) => (idx === i ? { ...f, status: "processing" } : f))
      );
      
      try {
        const formData = new FormData();
        formData.append("files", files[i].file);

        const response = await fetch(`${BACKEND_URL}/api/documents/upload`, {
          method: "POST",
          body: formData,
        });

        if (!response.ok) throw new Error(`HTTP ${response.status}`);

        const reader = response.body?.getReader();
        if (!reader) throw new Error("Không thể đọc luồng dữ liệu từ server");

        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const lines = buffer.split("\n\n");
          buffer = lines.pop() || "";

          for (const line of lines) {
            if (line.startsWith("data: ")) {
              const data = JSON.parse(line.substring(6));
              
              if (data.type === "progress") {
                // Tính toán phần trăm tổng thể dựa trên tiến độ của file hiện tại
                // Giả sử mỗi file chiếm 1 phần bằng nhau của thanh tiến trình tổng
                const perFileWeight = 100 / files.length;
                const baseProgress = (i / files.length) * 100;
                const fileInternalProgress = (data.percent / 100) * perFileWeight;
                
                setFakeProgress(baseProgress + fileInternalProgress);
                setCurrentStepMessage(data.message);
              } else if (data.type === "result") {
                const result = data.results?.[0];
                setFiles((prev) =>
                  prev.map((f, idx) =>
                    idx === i
                      ? {
                          ...f,
                          status: result?.status === "Success" ? "done" : "error",
                          message: result?.status,
                          chunkCount: result?.chunkCount,
                        }
                      : f
                  )
                );
              }
            }
          }
        }
      } catch (error: any) {
        setFiles((prev) =>
          prev.map((f, idx) =>
            idx === i ? { ...f, status: "error", message: error.message } : f
          )
        );
      }
      setOverallProgress((prev) => ({ ...prev, current: i + 1 }));
    }

    setFakeProgress(100);
    setIsProcessing(false);
    setCurrentStepMessage("Tất cả đã hoàn tất!");
  };

  const handleClose = () => {
    if (isProcessing) return;
    setFiles([]);
    setOverallProgress({ current: 0, total: 0 });
    onClose();
  };

  const pendingCount = files.filter((f) => f.status === "pending").length;
  const doneCount = files.filter((f) => f.status === "done").length;
  const progressPercent = overallProgress.total > 0 ? Math.round((overallProgress.current / overallProgress.total) * 100) : 0;

  if (!isOpen) return null;

  return (
    <AnimatePresence>
      {isOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="fixed inset-0 z-[100] flex items-center justify-center p-4"
        >
          {/* Backdrop */}
          <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" onClick={handleClose} />

          {/* Modal */}
          <motion.div
            initial={{ opacity: 0, scale: 0.95, y: 20 }}
            animate={{ opacity: 1, scale: 1, y: 0 }}
            exit={{ opacity: 0, scale: 0.95, y: 20 }}
            transition={{ type: "spring", damping: 25, stiffness: 300 }}
            className="relative w-full max-w-xl bg-white rounded-2xl sm:rounded-3xl border border-slate-200 shadow-[0_20px_50px_rgba(0,0,0,0.1)] overflow-hidden mx-2 sm:mx-0"
          >
            {/* Header */}
            <div className="flex items-center justify-between px-6 py-4 border-b border-primary/5">
              <div className="flex items-center gap-2 sm:gap-3">
                <div className="w-8 h-8 sm:w-9 sm:h-9 rounded-lg sm:rounded-xl bg-gradient-to-br from-primary to-accent flex items-center justify-center">
                  <UploadSimple size={18} weight="bold" className="text-white" />
                </div>
                <div>
                  <h2 className="text-xs sm:text-sm font-bold text-foreground">Import Data</h2>
                  <p className="text-[9px] sm:text-[10px] text-muted-foreground">PDF, TXT, JSON</p>
                </div>
              </div>
              <button
                onClick={handleClose}
                disabled={isProcessing}
                className="w-8 h-8 rounded-xl flex items-center justify-center hover:bg-primary/5 text-muted-foreground hover:text-foreground transition-all disabled:opacity-50"
              >
                <X size={16} weight="bold" />
              </button>
            </div>

            {/* Drag & Drop Zone */}
            <div className="px-6 pt-5 pb-3">
              <div
                onDrop={handleDrop}
                onDragOver={(e) => e.preventDefault()}
                onClick={() => fileInputRef.current?.click()}
                className="border-2 border-dashed border-primary/15 hover:border-primary/40 rounded-xl sm:rounded-2xl p-4 sm:p-8 text-center transition-all hover:bg-primary/3 group"
              >
                <UploadSimple size={28} weight="duotone" className="mx-auto text-primary/40 group-hover:text-primary/70 transition-colors mb-2 sm:mb-3" />
                <p className="text-[11px] sm:text-xs font-semibold text-foreground/70">Kéo thả file vào đây</p>
                <p className="text-[9px] sm:text-[10px] text-muted-foreground mt-1">hoặc click để chọn file</p>
              </div>
              <input
                ref={fileInputRef}
                type="file"
                multiple
                accept=".pdf,.txt,.json"
                onChange={(e) => handleFilesSelected(e.target.files)}
                className="hidden"
              />
            </div>

            {/* File List */}
            {files.length > 0 && (
              <div className="px-6 pb-3 max-h-52 overflow-y-auto scrollbar-custom">
                <div className="space-y-2">
                  {files.map((item, index) => (
                    <div
                      key={item.file.name}
                      className={cn(
                        "flex items-center justify-between px-3 py-2.5 rounded-xl border transition-all",
                        item.status === "done" && "bg-emerald-50 border-emerald-200",
                        item.status === "error" && "bg-red-50 border-red-200",
                        item.status === "processing" && "bg-primary/5 border-primary/20",
                        item.status === "pending" && "bg-white/50 border-primary/5"
                      )}
                    >
                      <div className="flex items-center gap-2.5 min-w-0">
                        <div className="shrink-0">
                          {item.status === "pending" && <File size={18} weight="duotone" className="text-primary/50" />}
                          {item.status === "processing" && <SpinnerGap size={18} weight="bold" className="text-primary animate-spin" />}
                          {item.status === "done" && <CheckCircle size={18} weight="fill" className="text-emerald-500" />}
                          {item.status === "error" && <Warning size={18} weight="fill" className="text-red-500" />}
                        </div>
                        <div className="min-w-0">
                          <p className="text-[11px] font-semibold text-foreground truncate">{item.file.name}</p>
                          <p className="text-[9px] text-muted-foreground">
                            {item.status === "pending" && `${(item.file.size / 1024).toFixed(1)} KB`}
                            {item.status === "processing" && "Đang xử lý..."}
                            {item.status === "done" && `${item.chunkCount} chunks`}
                            {item.status === "error" && (item.message || "Lỗi")}
                          </p>
                        </div>
                      </div>
                      {item.status === "pending" && !isProcessing && (
                        <button
                          onClick={() => handleRemoveFile(index)}
                          className="shrink-0 w-7 h-7 rounded-lg flex items-center justify-center hover:bg-red-50 text-muted-foreground hover:text-red-500 transition-all"
                        >
                          <Trash size={14} weight="bold" />
                        </button>
                      )}
                    </div>
                  ))}
                </div>
              </div>
            )}

            {/* Progress Bar Container */}
            {isProcessing && (
              <div className="px-6 pb-5">
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center gap-2">
                    <SpinnerGap size={12} weight="bold" className="text-primary animate-spin" />
                    <span className="text-[10px] font-bold text-primary uppercase tracking-wider">
                      {currentStepMessage || `Processing ${overallProgress.current + 1} of ${overallProgress.total}`}
                    </span>
                  </div>
                  <span className="text-[11px] font-black text-primary drop-shadow-sm">{Math.round(fakeProgress)}%</span>
                </div>
                
                {/* Outer Track */}
                <div className="relative w-full h-3 bg-primary/10 rounded-full overflow-hidden border border-primary/5 p-[2px]">
                  {/* Inner Bar */}
                  <motion.div
                    className="relative h-full bg-gradient-to-r from-primary via-accent to-primary bg-[length:200%_100%] rounded-full shadow-[0_0_12px_rgba(var(--primary-rgb),0.3)]"
                    initial={{ width: "2%" }}
                    animate={{ 
                      width: `${Math.max(fakeProgress, 5)}%`,
                      backgroundPosition: ["0% 0%", "200% 0%"]
                    }}
                    transition={{ 
                      width: { duration: 0.5, ease: "linear" },
                      backgroundPosition: { duration: 2, repeat: Infinity, ease: "linear" }
                    }}
                  >
                    {/* Shimmer Highlight */}
                    <div className="absolute inset-0 bg-gradient-to-r from-transparent via-white/30 to-transparent w-full" />
                  </motion.div>
                </div>
                <p className="text-[9px] text-muted-foreground mt-2 text-center italic">
                  Vui lòng không đóng cửa sổ khi đang xử lý dữ liệu.
                </p>
              </div>
            )}

            {/* Footer */}
            <div className="flex items-center justify-between px-6 py-4 border-t border-primary/5">
              <p className="text-[10px] text-muted-foreground">
                {files.length === 0 && "Chưa có file nào được chọn"}
                {files.length > 0 && !isProcessing && doneCount === 0 && `${pendingCount} file sẵn sàng`}
                {files.length > 0 && !isProcessing && doneCount > 0 && `Hoàn tất ${doneCount}/${files.length} file`}
              </p>
              <button
                onClick={handleStartProcessing}
                disabled={pendingCount === 0 || isProcessing}
                className={cn(
                  "flex items-center gap-1.5 sm:gap-2 px-3 sm:px-5 py-2 rounded-xl text-[10px] sm:text-[11px] font-bold transition-all active:scale-95",
                  pendingCount > 0 && !isProcessing
                    ? "bg-gradient-to-r from-primary to-accent text-white shadow-lg shadow-primary/20 hover:shadow-xl hover:shadow-primary/30"
                    : "bg-primary/10 text-primary/40 cursor-not-allowed"
                )}
              >
                <Lightning size={14} weight="fill" />
                <span className="hidden sm:inline">Bắt đầu xử lý</span>
                <span className="sm:hidden">Bắt đầu</span>
              </button>
            </div>
          </motion.div>
        </motion.div>
      )}
    </AnimatePresence>
  );
};
