import * as React from "react";
import { List, UploadSimple } from "@phosphor-icons/react";
import { cn } from "@/lib/utils";
import { motion } from "framer-motion";

interface ChatHeaderProps {
  onOpenSidebar: () => void;
  onOpenUpload: () => void;
  apiMode: string | undefined;
  isApiConnected?: boolean | null;
}

export const ChatHeader: React.FC<ChatHeaderProps> = ({ onOpenSidebar, onOpenUpload, apiMode, isApiConnected }) => {
  return (
    <motion.header 
      initial={{ opacity: 0, y: -20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.8, ease: "easeOut" }}
      className="fixed top-2 sm:top-6 left-0 right-0 z-50 px-2 sm:px-8 flex justify-between items-center pointer-events-none"
    >
      <div className="flex items-center gap-2 sm:gap-4 pointer-events-auto">
        <button 
          onClick={onOpenSidebar}
          className="w-10 h-10 sm:w-11 sm:h-11 flex items-center justify-center rounded-xl sm:rounded-2xl glass-panel border border-primary/10 text-foreground/60 hover:text-primary hover:border-primary/30 transition-all shadow-sm active:scale-90"
        >
          <List size={22} weight="bold" />
        </button>
        <div className="flex items-center gap-2 sm:gap-3">
          <h1 className="text-xs sm:text-base font-black tracking-[0.2em] text-foreground uppercase">
            DO<span className="text-primary">DO</span>
          </h1>
          <div className="flex items-center gap-1 sm:gap-2">
            <div className="h-3 w-[1px] bg-foreground/10 hidden sm:block" />
            <div className={cn(
              "px-1.5 sm:px-2 py-0.5 rounded-full text-[7px] sm:text-[9px] font-bold uppercase tracking-wider border flex items-center gap-1 sm:gap-1.5",
              apiMode === 'dotnet' 
                ? isApiConnected === false 
                  ? "bg-red-500/10 text-red-500 border-red-500/20"
                  : "bg-blue-500/10 text-blue-500 border-blue-500/20" 
                : "bg-amber-500/10 text-amber-500 border-amber-500/20"
            )}>
              {apiMode === 'dotnet' && (
                <div className={cn(
                  "w-1 sm:w-1.5 h-1 sm:h-1.5 rounded-full animate-pulse",
                  isApiConnected === false ? "bg-red-500" : (isApiConnected === true ? "bg-blue-500" : "bg-blue-500/50")
                )} />
              )}
              <span className="hidden xs:inline">
                {apiMode === 'dotnet' 
                  ? (isApiConnected === false ? ".NET API (Offline)" : ".NET API") 
                  : "Direct"}
              </span>
              <span className="xs:hidden">
                {apiMode === 'dotnet' ? "API" : "DIR"}
              </span>
            </div>
          </div>
        </div>
      </div>

      <div className="flex items-center gap-2 pointer-events-auto">
        <div className="flex items-center p-0.5 sm:p-1 rounded-full glass-panel border border-primary/5 shadow-sm">
          <button 
            onClick={onOpenUpload}
            className="px-2 sm:px-4 py-1.5 rounded-full text-[9px] sm:text-[10px] font-black text-muted-foreground/60 hover:text-primary transition-all active:scale-95 uppercase tracking-[0.15em] flex items-center gap-1.5"
          >
            <UploadSimple size={12} weight="bold" />
            <span className="hidden sm:inline">Import</span>
          </button>
          <div className="h-4 w-[1px] bg-foreground/10" />
          <button 
            onClick={() => window.location.reload()}
            className="px-2 sm:px-4 py-1.5 rounded-full text-[9px] sm:text-[10px] font-black text-muted-foreground/60 hover:text-primary transition-all active:scale-95 uppercase tracking-[0.15em]"
          >
            <span className="hidden sm:inline">New Chat</span>
            <span className="sm:hidden">New</span>
          </button>
        </div>
      </div>
    </motion.header>
  );
};
