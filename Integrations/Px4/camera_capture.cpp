#include <gz/msgs/image.pb.h>
#include <gz/transport/Node.hh>
#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>

#include <atomic>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace fs = std::filesystem;

class LatestJpeg
{
public:
    LatestJpeg(fs::path output, int quality, double maxRate)
        : output_(std::move(output)), quality_(quality), minimumPeriod_(1.0 / maxRate) {}

    void OnImage(const gz::msgs::Image &message)
    {
        const auto now = std::chrono::steady_clock::now();
        if (last_ != std::chrono::steady_clock::time_point{} &&
            std::chrono::duration<double>(now - last_).count() < minimumPeriod_)
            return;

        cv::Mat source;
        int conversion = -1;
        const int width = static_cast<int>(message.width());
        const int height = static_cast<int>(message.height());
        const auto format = message.pixel_format_type();
        if (width <= 0 || height <= 0 || message.data().empty()) return;

        switch (format)
        {
            case gz::msgs::PixelFormatType::RGB_INT8:
                source = cv::Mat(height, width, CV_8UC3, const_cast<char *>(message.data().data()));
                conversion = cv::COLOR_RGB2BGR;
                break;
            case gz::msgs::PixelFormatType::BGR_INT8:
                source = cv::Mat(height, width, CV_8UC3, const_cast<char *>(message.data().data()));
                break;
            case gz::msgs::PixelFormatType::RGBA_INT8:
                source = cv::Mat(height, width, CV_8UC4, const_cast<char *>(message.data().data()));
                conversion = cv::COLOR_RGBA2BGR;
                break;
            case gz::msgs::PixelFormatType::BGRA_INT8:
                source = cv::Mat(height, width, CV_8UC4, const_cast<char *>(message.data().data()));
                conversion = cv::COLOR_BGRA2BGR;
                break;
            case gz::msgs::PixelFormatType::L_INT8:
                source = cv::Mat(height, width, CV_8UC1, const_cast<char *>(message.data().data()));
                break;
            default:
                if (!warned_.exchange(true))
                    std::cerr << "Unsupported Gazebo pixel format " << format << '\n';
                return;
        }

        cv::Mat bgr;
        if (conversion >= 0) cv::cvtColor(source, bgr, conversion);
        else bgr = source;
        std::vector<unsigned char> encoded;
        if (!cv::imencode(".jpg", bgr, encoded, {cv::IMWRITE_JPEG_QUALITY, quality_})) return;

        const fs::path temporary = output_.string() + ".tmp";
        {
            std::ofstream stream(temporary, std::ios::binary | std::ios::trunc);
            stream.write(reinterpret_cast<const char *>(encoded.data()), static_cast<std::streamsize>(encoded.size()));
            stream.flush();
            if (!stream) return;
        }
        fs::rename(temporary, output_);
        last_ = now;
        frames_++;
    }

    std::uint64_t Frames() const { return frames_.load(); }

private:
    fs::path output_;
    int quality_;
    double minimumPeriod_;
    std::chrono::steady_clock::time_point last_{};
    std::atomic<bool> warned_{false};
    std::atomic<std::uint64_t> frames_{0};
};

int main(int argc, char **argv)
{
    if (argc != 4)
    {
        std::cerr << "usage: camera_capture TOPIC OUTPUT.jpg MAX_FPS\n";
        return 2;
    }
    const double maxRate = std::stod(argv[3]);
    if (maxRate <= 0) return 2;
    LatestJpeg capture(argv[2], 80, maxRate);
    gz::transport::Node node;
    if (!node.Subscribe(argv[1], &LatestJpeg::OnImage, &capture))
    {
        std::cerr << "Unable to subscribe to " << argv[1] << '\n';
        return 3;
    }
    std::cout << "Subscribed " << argv[1] << " -> " << argv[2] << '\n';
    while (true) std::this_thread::sleep_for(std::chrono::seconds(1));
}
