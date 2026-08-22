namespace NAPS2.PartyDossier;

/// <summary>
/// CCP Scan catalog of Party-member dossier document types, migrated exactly from the existing
/// Tool Ho tro so hoa CSDL Dang vien Tuy Hoa v10.2.1 business rules.
/// </summary>
public static class PartyDossierDocumentCatalog
{
    private static readonly IReadOnlyList<PartyDossierDocumentType> _all = new[]
    {
        new PartyDossierDocumentType(1, "Ly lich nguoi xin vao dang", true),
        new PartyDossierDocumentType(2, "Ly lich dang vien", true),
        new PartyDossierDocumentType(3, "Phieu dang vien", true),
        new PartyDossierDocumentType(4, "Phieu bo sung ho so dang vien"),
        new PartyDossierDocumentType(5, "Quyet dinh ket nap dang vien", true),
        new PartyDossierDocumentType(6, "Quyet dinh ket nap lai nguoi vao dang"),
        new PartyDossierDocumentType(7, "Quyet dinh cong nhan dang vien chinh thuc", true),
        new PartyDossierDocumentType(8, "Quyet dinh cua cap uy co tham quyen xoa ten dang vien du bi"),
        new PartyDossierDocumentType(9, "Quyet dinh cong nhan dang vien sau khi khac phuc thuc hien dung quy dinh ve tham quyen thu tuc ket nap dang vien"),
        new PartyDossierDocumentType(10, "Quyet dinh cong nhan dang vien chinh thuc sau khi khac phuc thuc hien dung quy dinh ve tham quyen thu tuc"),
        new PartyDossierDocumentType(11, "Quyet dinh huy quyet dinh ket nap dang vien sai quy dinh khong dung tieu chuan dieu kien"),
        new PartyDossierDocumentType(12, "Quyet dinh huy quyet dinh ket nap lai dang vien sai quy dinh khong dung tieu chuan dieu kien"),
        new PartyDossierDocumentType(13, "Quyet dinh huy quyet dinh cong nhan dang vien chinh thuc sai quy dinh khong dung tieu chuan dieu kien"),
        new PartyDossierDocumentType(14, "Quyet dinh khoi phuc quyen cua dang vien"),
        new PartyDossierDocumentType(15, "Quyet dinh noi lai sinh hoat dang cua dang vien"),
        new PartyDossierDocumentType(16, "Quyet dinh xoa ten trong danh sach dang vien"),
        new PartyDossierDocumentType(17, "Quyet dinh cho dang vien ra khoi dang"),
        new PartyDossierDocumentType(18, "Giay xac nhan tuoi dang"),
        new PartyDossierDocumentType(19, "Quyet dinh phat the dang vien"),
        new PartyDossierDocumentType(20, "Quyet dinh tang huy hieu dang"),
        new PartyDossierDocumentType(21, "Quyet dinh truy tang huy hieu dang"),
        new PartyDossierDocumentType(22, "Quyet dinh ky luat dang"),
        new PartyDossierDocumentType(23, "Quyet dinh khen thuong"),
        new PartyDossierDocumentType(24, "Quyet dinh dinh chi sinh hoat dang"),
        new PartyDossierDocumentType(25, "Quyet dinh dinh chi cap uy"),
        new PartyDossierDocumentType(26, "Quyet dinh gia han dinh chi sinh hoat dang"),
        new PartyDossierDocumentType(27, "Quyet dinh gia han dinh chi cap uy"),
        new PartyDossierDocumentType(28, "Quyet dinh giai quyet khieu nai"),
        new PartyDossierDocumentType(29, "Quyet dinh giai quyet to cao"),
        new PartyDossierDocumentType(30, "Quyet dinh dinh chi chuc vu trong dang"),
        new PartyDossierDocumentType(31, "Quyet dinh cho tro lai sinh hoat cap uy"),
        new PartyDossierDocumentType(32, "Thong bao ket luan giai quyet to cao"),
        new PartyDossierDocumentType(33, "Thong bao khong giai quyet khieu nai ky luat dang"),
        new PartyDossierDocumentType(34, "Ket luan kiem tra khi co dau hieu vi pham"),
        new PartyDossierDocumentType(35, "Ket luan minh oan khong vi pham"),
        new PartyDossierDocumentType(36, "Quyet dinh ky luat hanh chinh"),
        new PartyDossierDocumentType(37, "Don xin vao dang"),
        new PartyDossierDocumentType(38, "Giay chung nhan hoc lop nhan thuc ve dang"),
        new PartyDossierDocumentType(39, "Giay gioi thieu nguoi vao dang"),
        new PartyDossierDocumentType(40, "Nghi quyet gioi thieu doan vien uu tu vao dang"),
        new PartyDossierDocumentType(41, "Nghi quyet gioi thieu doan vien cong doan vao dang"),
        new PartyDossierDocumentType(42, "Tong hop y kien nhan xet doi voi nguoi vao dang"),
        new PartyDossierDocumentType(43, "Nghi quyet de nghi ket nap dang vien cua chi bo"),
        new PartyDossierDocumentType(44, "Bao cao tham dinh nghi quyet de nghi ket nap dang vien"),
        new PartyDossierDocumentType(45, "Nghi quyet de nghi ket nap dang vien cua dang uy co so"),
        new PartyDossierDocumentType(46, "Giay chung nhan hoc lop dang vien moi"),
        new PartyDossierDocumentType(47, "Ban tu kiem diem dang vien du bi"),
        new PartyDossierDocumentType(48, "Ban nhan xet dang vien du bi"),
        new PartyDossierDocumentType(49, "Tong hop y kien nhan xet dang vien du bi"),
        new PartyDossierDocumentType(50, "Nghi quyet de nghi cong nhan dang vien chinh thuc"),
        new PartyDossierDocumentType(51, "Bao cao tham dinh nghi quyet cong nhan dang vien chinh thuc"),
        new PartyDossierDocumentType(52, "Nghi quyet de nghi cong nhan dang vien chinh thuc cua dang uy co so"),
        new PartyDossierDocumentType(53, "Giay chung nhan nguoi vao dang"),
        new PartyDossierDocumentType(54, "Giay gioi thieu sinh hoat dang chinh thuc"),
        new PartyDossierDocumentType(55, "Giay gioi thieu sinh hoat dang tam thoi"),
        new PartyDossierDocumentType(56, "Giay gioi thieu sinh hoat dang ra ngoai nuoc"),
        new PartyDossierDocumentType(57, "Phieu cong tac chinh thuc ngoai nuoc"),
        new PartyDossierDocumentType(58, "Phieu cong tac tam thoi ngoai nuoc"),
        new PartyDossierDocumentType(59, "Giay gioi thieu sinh hoat dang noi bo"),
        new PartyDossierDocumentType(60, "Phieu bao dang vien chuyen sinh hoat dang chinh thuc"),
        new PartyDossierDocumentType(61, "Ban tu kiem diem hang nam"),
        new PartyDossierDocumentType(62, "Ban tu kiem diem khi chuyen sinh hoat dang"),
        new PartyDossierDocumentType(63, "Ban kiem diem sinh hoat dang o nuoc ngoai"),
        new PartyDossierDocumentType(64, "Quyet dinh bieu duong dang vien"),
        new PartyDossierDocumentType(65, "Ban tu kiem diem dang vien vi pham"),
        new PartyDossierDocumentType(66, "Thong bao vi pham cua co quan phap luat"),
        new PartyDossierDocumentType(67, "Quyet dinh giai quyet khieu nai ky luat hanh chinh"),
        new PartyDossierDocumentType(68, "Ban an hinh su co hieu luc"),
        new PartyDossierDocumentType(69, "Ket luan thanh tra kiem toan"),
        new PartyDossierDocumentType(70, "Bang chung chi ly luan chinh tri"),
        new PartyDossierDocumentType(71, "Quyet dinh cap lai huy hieu dang"),
        new PartyDossierDocumentType(72, "Quyet dinh cap lai the dang vien"),
        new PartyDossierDocumentType(73, "Quyet dinh doi lai the dang vien"),
        new PartyDossierDocumentType(74, "Quyet dinh thay doi ho ten"),
        new PartyDossierDocumentType(75, "Khai sinh goc hoac dinh chinh ngay sinh"),
        new PartyDossierDocumentType(76, "Xac nhan thay doi que quan noi cu tru"),
        new PartyDossierDocumentType(77, "Quyet dinh xac dinh lai dan toc"),
        new PartyDossierDocumentType(78, "Ket luan huy van bang"),
        new PartyDossierDocumentType(79, "Don xin mien cong tac va sinh hoat dang"),
        new PartyDossierDocumentType(80, "Giay xac nhan co so y te"),
        new PartyDossierDocumentType(81, "Nghi quyet mien cong tac va sinh hoat dang"),
        new PartyDossierDocumentType(82, "Van ban bo sung ly lich sau khi ve nuoc"),
        new PartyDossierDocumentType(83, "Giay chung tu dang vien"),
        new PartyDossierDocumentType(84, "Ban tuong trinh mat ho so dang vien"),
        new PartyDossierDocumentType(85, "Phieu dang vien cu"),
        new PartyDossierDocumentType(86, "Cac van bang chung chi"),
        new PartyDossierDocumentType(87, "Cac quyet dinh dieu dong bo nhiem"),
        new PartyDossierDocumentType(88, "Quyet dinh nghi huu"),
        new PartyDossierDocumentType(89, "Quyet dinh nghi mat suc lao dong"),
        new PartyDossierDocumentType(90, "Quyet dinh phuc vien chuyen nganh"),
        new PartyDossierDocumentType(91, "Quyet dinh xuat ngu"),
        new PartyDossierDocumentType(92, "Cong van gioi thieu nguoi vao dang cua cap uy co so"),
        new PartyDossierDocumentType(93, "Cong van gioi thieu nguoi vao dang cua cap uy co tham quyen"),
        new PartyDossierDocumentType(94, "Cong van gioi thieu nguoi vao dang da duoc ket nap"),
        new PartyDossierDocumentType(95, "Cong van gioi thieu nguoi vao dang chuyen ra ngoai dang bo cap xa"),
        new PartyDossierDocumentType(96, "Giay gioi thieu di tham tra ly lich"),
        new PartyDossierDocumentType(97, "Cong van de nghi tham tra ly lich"),
        new PartyDossierDocumentType(98, "Cong van chi dao lam lai thu tuc ket nap dang vien"),
        new PartyDossierDocumentType(99, "Cong van chi dao lam lai thu tuc cong nhan dang vien chinh thuc"),
        new PartyDossierDocumentType(100, "Phieu bao dang vien duoc cong nhan chinh thuc"),
        new PartyDossierDocumentType(101, "Phieu bao dang vien ra khoi dang"),
        new PartyDossierDocumentType(102, "Phieu bao dang vien tu tran"),
        new PartyDossierDocumentType(103, "To khai de nghi tang huy hieu dang"),
        new PartyDossierDocumentType(104, "To khai de nghi truy tang huy hieu dang")
    };

    private static readonly IReadOnlyDictionary<int, PartyDossierDocumentType> _byId =
        _all.ToDictionary(x => x.Id);

    public static IReadOnlyList<PartyDossierDocumentType> All => _all;

    public static IReadOnlyList<PartyDossierDocumentType> Required => _all.Where(x => x.IsRequired).ToList();

    public static PartyDossierDocumentType? Get(int id) => _byId.TryGetValue(id, out var item) ? item : null;

    public static string? GetStandardizedBaseName(int id)
    {
        var item = Get(id);
        return item == null ? null : $"{item.Id:00}.{item.Name}";
    }

    public static IReadOnlyList<PartyDossierDocumentType> GetMissingRequired(IEnumerable<int> presentDocumentTypeIds)
    {
        var present = new HashSet<int>(presentDocumentTypeIds);
        return Required.Where(x => !present.Contains(x.Id)).ToList();
    }
}

public sealed class PartyDossierDocumentType
{
    public PartyDossierDocumentType(int id, string name, bool isRequired = false)
    {
        Id = id;
        Name = name;
        IsRequired = isRequired;
    }

    public int Id { get; }

    public string Name { get; }

    public bool IsRequired { get; }

    public string DisplayName => $"{Id:00}. {Name}";

    public string StandardizedBaseName => $"{Id:00}.{Name}";
}
