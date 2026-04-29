import type { Metadata } from "next";
import { Be_Vietnam_Pro, Fira_Code } from "next/font/google";
import "./globals.css";

const beVietnamPro = Be_Vietnam_Pro({
  variable: "--font-be-vietnam",
  subsets: ["vietnamese", "latin"],
  weight: ["100", "200", "300", "400", "500", "600", "700", "800", "900"],
});

const firaCode = Fira_Code({
  variable: "--font-fira-code",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  title: "SQL AI Insight",
  description: "Truy vấn dữ liệu dễ dàng với sức mạnh từ AI",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="vi">
      <body
        className={`${beVietnamPro.variable} ${firaCode.variable} min-h-full flex flex-col font-sans bg-background text-foreground antialiased`}
      >
        {children}
      </body>
    </html>
  );
}
